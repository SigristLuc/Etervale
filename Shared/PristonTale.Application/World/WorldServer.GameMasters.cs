using System.Numerics;
using PristonTale.Application.GameMasters;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Spawning;

namespace PristonTale.Application.World;

// World state the game-master commands read and change. Every call runs on the command phase; the requester's quota
// counts each one.
public sealed partial class WorldServer
{
    // A game master's monster stays this long as a pet.
    public const float GameMasterPetLifetimeMs = 4 * 60 * 1000;

    // Extra monsters of one call appear within this distance of the game master on each axis.
    public const float GameMasterSpawnScatter = 64f;

    // The status a game master's absorption change puts on a monster; it has no icon.
    public const uint GameMasterAbsorptionStatus = 0xE7E00001;

    // The minute of each hour at which scheduled bosses appear today (an int is read atomically off the tick).
    public int BossMinute => _bossSchedule.Minute;

    // The server's clock, local and in Unix seconds, as the world reads it.
    public DateTimeOffset LocalNow => _clock.GetLocalNow();

    public long UnixSeconds => _clock.GetUtcNow().ToUnixTimeSeconds();

    // The display name of a field (the catalog is immutable, so any thread may read it).
    public string FieldName(int fieldId) => _catalog.TryGetFieldByCode(fieldId, out var definition) ? definition.Name : fieldId.ToString();

    // Gold spent on services since the server started, for the currency report.
    public CurrencyLedger Currency { get; } = new();

    private void RecordSpending(PreparedNpcOperation operation)
    {
        if (operation.MoneyDelta >= 0 || operation.Candidate is null || operation.Candidate.ServiceOperations.Count == 0)
        {
            return;
        }

        var command = operation.Candidate.ServiceOperations[^1].Command;
        var spent = -operation.MoneyDelta;
        switch (command.Action)
        {
            case Simulation.World.Npcs.NpcServiceAction.Buy:
                var key = _worldData.Current.Items.TryGet(command.ItemCode, out var definition) ? definition.CatalogKey : "";
                Currency.Record(CurrencyLedger.ShopSinkFor(key), spent);
                break;
            case Simulation.World.Npcs.NpcServiceAction.Teleport:
                Currency.Record(CurrencySink.Teleports, spent);
                break;
        }
    }

    public Task<GmPlacement?> LocateAsync(ulong requesterId, ulong entityId)
    {
        return EnqueueAsync<GmPlacement?>(requesterId, () =>
            _connections.TryGetValue(entityId, out var connection) ? new GmPlacement(connection.FieldId, connection.Player.Position) : null);
    }

    public Task<bool> KillAsync(ulong requesterId, ulong entityId)
    {
        return EnqueueAsync(requesterId, () => _connections.TryGetValue(entityId, out var connection) && connection.Player.Kill());
    }

    public Task<bool> SetHiddenAsync(ulong entityId, bool hidden)
    {
        return EnqueueAsync(entityId, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection))
            {
                return false;
            }

            connection.Player.Hidden = hidden;
            return true;
        });
    }

    // Where a field warp puts a game master: a random start point of a castle, the first start point elsewhere, else the
    // field's centre, on the highest floor there. Null for a field code the catalog does not know.
    public GmPlacement? FieldArrival(int fieldCode, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!_catalog.TryGetFieldByCode(fieldCode, out var definition))
        {
            return null;
        }

        var points = definition.StartPoints;
        var point = points.Count == 0 ? definition.Center
            : definition.State == FieldState.Castle ? points[random.Next(points.Count)]
            : points[0];
        return new GmPlacement(fieldCode, new Vector3(point.X, float.NaN, point.Z));
    }

    // Places a player at a position of a field, as a game master's summon or approach. A position without a height
    // (NaN) stands on the highest floor there. The caller sends the forced warp.
    public Task<GmPlacement?> RelocateAsync(ulong requesterId, ulong entityId, GmPlacement destination)
    {
        return EnqueueAsync<GmPlacement?>(requesterId, () =>
        {
            var onFloor = float.IsNaN(destination.Position.Y);
            var requested = onFloor ? destination.Position with { Y = 0f } : destination.Position;
            if (!_connections.TryGetValue(entityId, out var connection) || connection.Player.IsDead
                || !_catalog.TryGetFieldByCode(destination.FieldId, out var definition) || !WorldPosition.IsValid(requested))
            {
                return null;
            }

            var player = connection.Player;
            var target = GetField(destination.FieldId);
            TransferPlayer(player, _fields[connection.FieldId], target);
            var position = onFloor ? target.Terrain.PlaceOnFloor(requested) : target.Terrain.SettleNear(requested, FieldTerrain.DropTolerance * 2f);
            var inVillage = definition.State == FieldState.Village;
            player.EnterField(position, inVillage);
            _membershipChanged = true;
            _connections[player.EntityId] = connection with { FieldId = destination.FieldId };
            UpdatePlacement(connection.Session, destination.FieldId, position, inVillage);
            return new GmPlacement(destination.FieldId, position);
        });
    }

    // Players in the world and living wild monsters of every field.
    public Task<(int Players, int Monsters)> CountsAsync(ulong requesterId)
    {
        return EnqueueAsync(requesterId, () => (_connections.Count, _fields.Values.Sum(static field => field.LiveMonsterCount)));
    }

    // The requester's field and how many players are in it.
    public Task<(int FieldId, int Players)> FieldPlayersAsync(ulong requesterId)
    {
        return EnqueueAsync(requesterId, () =>
        {
            if (!_connections.TryGetValue(requesterId, out var connection))
            {
                return (0, 0);
            }

            var fieldId = connection.FieldId;
            return (fieldId, _connections.Values.Count(other => other.FieldId == fieldId));
        });
    }

    // Calls count + 1 monsters of a name at the game master: the first on the spot, the others scattered around. A pet takes
    // the game master's side instead (one at a time). Monsters a low-level game master calls drop nothing.
    public Task<GmSpawnResult> SpawnMonstersAsync(ulong requesterId, string name, int extra, bool pet, bool drops)
    {
        return EnqueueAsync(requesterId, () =>
        {
            if (!_connections.TryGetValue(requesterId, out var connection))
            {
                return new GmSpawnResult(false, 0);
            }

            var field = _fields[connection.FieldId];
            if (!field.TryFindMonsterDefinition(name, out var definition))
            {
                return new GmSpawnResult(false, 0);
            }

            var origin = connection.Player.Position;
            if (pet)
            {
                field.CallGameMasterPet(connection.Player, definition, GameMasterPetLifetimeMs);
                return new GmSpawnResult(true, 1);
            }

            field.SpawnForGameMaster(definition, origin, drops);
            var spawned = 1;
            for (var index = 0; index < extra; index++)
            {
                var x = field.Rng.NextUnit() * 2f * GameMasterSpawnScatter - GameMasterSpawnScatter;
                var z = field.Rng.NextUnit() * 2f * GameMasterSpawnScatter - GameMasterSpawnScatter;
                field.SpawnForGameMaster(definition, origin + new Vector3(x, 0f, z), drops);
                spawned++;
            }

            return new GmSpawnResult(true, spawned);
        });
    }

    // Starts (minutes above 0) or reads the field's event monster. A start with an unknown name fails.
    public Task<GmEventMonster?> EventMonsterAsync(ulong requesterId, string? name, int minutes, int permille)
    {
        return EnqueueAsync<GmEventMonster?>(requesterId, () =>
        {
            if (!_connections.TryGetValue(requesterId, out var connection))
            {
                return null;
            }

            var field = _fields[connection.FieldId];
            if (name is not null)
            {
                field.Spawner.EventMonster = null;
                if (!field.TryFindMonsterDefinition(name, out var definition))
                {
                    return null;
                }

                field.Spawner.EventMonster = new SpawnEvent(definition, field.NowMs + minutes * 60_000L, permille);
            }

            return field.Spawner.EventMonster is { } current && current.UntilMs > field.NowMs
                ? new GmEventMonster(current.Definition.Name, (current.UntilMs - field.NowMs) / 1000, current.Permille)
                : null;
        });
    }

    public Task<GmFieldMonsters?> FieldMonstersAsync(ulong requesterId)
    {
        return EnqueueAsync<GmFieldMonsters?>(requesterId, () =>
        {
            if (!_connections.TryGetValue(requesterId, out var connection))
            {
                return null;
            }

            var field = _fields[connection.FieldId];
            return new GmFieldMonsters(field.FieldId, field.Spawner.ActiveSpawnPointCount, field.SpawnPointMonsterCount, field.MonsterEntryCount,
                field.LiveMonsterCount, field.Spawner.SpawnPassIntervalMs / 1000);
        });
    }

    // Sets a monster's absorption (1 to 99) by a permanent status on it.
    public Task<GmAbsorption?> SetAbsorptionAsync(ulong requesterId, ulong monsterId, int absorption)
    {
        return EnqueueAsync<GmAbsorption?>(requesterId, () =>
        {
            foreach (var field in _fields.Values)
            {
                if (!field.TryGetMonster(monsterId, out var monster))
                {
                    continue;
                }

                var before = monster.Def.Combat.Absorption + monster.Effects.Totals.Flat(StatKind.Absorption);
                Span<StatModifier> modifiers = [StatModifier.Flat(StatKind.Absorption, absorption - monster.Def.Combat.Absorption)];
                var spec = new EffectSpec(GameMasterAbsorptionStatus, EffectSourceKind.System, harmful: false, modifiers);
                monster.Effects.Apply(spec, requesterId, float.PositiveInfinity, monster.EntityId);
                return new GmAbsorption(monster.Def.Name, before, absorption);
            }

            return null;
        });
    }

    // Drops an item copy, or gold when item is null, under the game master.
    public Task<bool> DropAsync(ulong requesterId, Func<ISimRng, ItemInstance?>? make, int gold)
    {
        return EnqueueAsync(requesterId, () =>
        {
            if (!_connections.TryGetValue(requesterId, out var connection))
            {
                return false;
            }

            var field = _fields[connection.FieldId];
            var item = make?.Invoke(field.Rng);
            if (make is not null && item is null)
            {
                return false;
            }

            return field.DropForGameMaster(item, gold, connection.Player.Position, requesterId);
        });
    }

    // Raises the character to the experience that starts a level; null when the level is out of range or not above the
    // current experience.
    public Task<ulong?> RaiseExperienceAsync(ulong entityId, int level)
    {
        return EnqueueAsync<ulong?>(entityId, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection) || level < 2 || level > ExperienceTable.MaximumLevel)
            {
                return null;
            }

            var character = connection.Player.Character;
            var target = ExperienceTable.ForLevel(level);
            // Below the final tier experience stops at level 100, as a monster's award would.
            if (character.Tier < 3)
            {
                target = Math.Min(target, ExperienceTable.ForLevel(100));
            }

            if (character.Experience < target)
            {
                character.AwardExperience(target - character.Experience);
            }

            return character.Experience;
        });
    }

    // Adds experience to a character as monsters award it (levels included); the new total, or null when the amount is
    // not positive.
    public Task<ulong?> AddExperienceAsync(ulong entityId, ulong amount)
    {
        return EnqueueAsync<ulong?>(entityId, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection) || amount == 0)
            {
                return null;
            }

            connection.Player.Character.AwardExperience(amount);
            return connection.Player.Character.Experience;
        });
    }

    // Ends one premium timer at once; false when it is not running.
    public Task<bool> EndPremiumAsync(ulong entityId, PremiumEffectKind kind)
    {
        return EnqueueAsync(entityId, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection) || !connection.Player.Character.Premium.IsActive(kind))
            {
                return false;
            }

            var premium = connection.Player.Character.Premium;
            premium.Restore(premium.Timers.Where(timer => timer.Kind != kind).ToArray());
            return true;
        });
    }

    // A game master's gift into a player's bag: the copy must fit the bag and the weight limit. Outcome false means it did
    // not fit (nothing is written); null means the player is not playing.
    public Task<PreparedCharacterOperation<bool>?> PrepareGiftAsync(ulong requesterId, PlayerSession session, Func<ISimRng, ItemInstance> make)
    {
        return EnqueueAsync<PreparedCharacterOperation<bool>?>(requesterId, () =>
        {
            if (!session.IsPlaying || session.Character is null || session.Loaded is null
                || !_connections.TryGetValue(session.EntityId, out var connection) || !ReferenceEquals(connection.Session, session))
            {
                return null;
            }

            var original = session.Character;
            var candidate = original.CloneForInventoryOperation();
            if (!candidate.ReceiveGroundLoot(make(_fields[connection.FieldId].Rng), 0))
            {
                return new PreparedCharacterOperation<bool>(original, null, null, false);
            }

            var snapshot = CharacterRecordFactory.Capture(session.Loaded, candidate, connection.FieldId, connection.Player.Position);
            return new PreparedCharacterOperation<bool>(original, candidate, snapshot, true);
        });
    }

    public Task<bool> CommitGiftAsync(PlayerSession session, PreparedCharacterOperation<bool> operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (!ReferenceEquals(session.Character, operation.Original) || operation.Candidate is null)
            {
                throw new InvalidOperationException("Character ownership changed during a game master's gift.");
            }

            operation.Original.CommitInventoryOperation(operation.Candidate);
            return true;
        }, waitForCapacity: true);
    }

    public Task<GmCharacterReport?> InspectAsync(ulong requesterId, ulong entityId)
    {
        return EnqueueAsync<GmCharacterReport?>(requesterId, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection))
            {
                return null;
            }

            var character = connection.Player.Character;
            var nowSeconds = _clock.GetUtcNow().ToUnixTimeSeconds();
            GmForceOrb? orb = null;
            foreach (var effect in character.Effects.Effects)
            {
                if (ForceOrbDefinition.IsOrbStatus(effect.Code))
                {
                    orb = new GmForceOrb((int)(effect.Code - ForceOrbDefinition.StatusBase), (int)(effect.RemainingMs / 1000f), effect.Spec.Magnitude);
                    break;
                }
            }

            var potions = new int[3, 4];
            for (var anchor = 0; anchor < character.Inventory.Capacity; anchor++)
            {
                if (character.Inventory[anchor] is not { } item)
                {
                    continue;
                }

                var key = item.Definition.CatalogKey;
                var kind = key.StartsWith("PL", StringComparison.OrdinalIgnoreCase) ? 0
                    : key.StartsWith("PM", StringComparison.OrdinalIgnoreCase) ? 1
                    : key.StartsWith("PS", StringComparison.OrdinalIgnoreCase) ? 2 : -1;
                if (kind >= 0 && key.Length == 5 && int.TryParse(key.AsSpan(2), out var number) && number is >= 101 and <= 104)
                {
                    potions[kind, number - 101] += item.Count;
                }
            }

            var derived = character.Derived;
            return new GmCharacterReport(connection.FieldId, connection.Player.Position, character.Money, orb,
                character.Premium.Find(PremiumEffectKind.ThirdEyes)?.RemainingSeconds(nowSeconds) ?? 0,
                character.Premium.Find(PremiumEffectKind.ExperienceUp)?.RemainingSeconds(nowSeconds) ?? 0,
                derived.AttackDamageMin, derived.AttackDamageMax, derived.CriticalChance, potions);
        });
    }
}
