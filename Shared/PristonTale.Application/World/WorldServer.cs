using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Npcs;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Channels;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;

namespace PristonTale.Application.World;

public sealed class WorldServer
{
    public const int TickHz = 20;

    private readonly Channel<Action> _commands = Channel.CreateBounded<Action>(new BoundedChannelOptions(65536)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    // Movement reports arrive at about 5 Hz per moving player; the budget bounds one tick's command phase.
    private const int CommandsPerTick = 16384;
    private readonly List<FieldInstance> _activeFields = new();
    private int _playerCount;
    private bool _membershipChanged;
    private IReadOnlyList<(int FieldId, int PlayerCount)> _activeCounts = Array.Empty<(int, int)>();
    private readonly Dictionary<int, FieldInstance> _fields = new();
    private readonly Dictionary<uint, Connection> _connections = new();
    private readonly EntityIdAllocator _ids = new();
    private readonly WorldDataHolder _worldData;
    private readonly WorldCatalog _catalog;
    private readonly TimeProvider _clock;
    private readonly long _clockOrigin;
    private readonly BossSpawnSchedule _bossSchedule;
    private readonly FieldTerrainCatalog _terrain;
    private readonly WorldServerOptions _options;
    // Commands each player has waiting in the shared queue; entries are dropped when the player leaves the world.
    private readonly ConcurrentDictionary<uint, PendingCommands> _pendingByPlayer = new();

    private sealed class PendingCommands
    {
        public int Count;
    }

    private sealed record Connection(
        int FieldId,
        WorldPlayer Player,
        ChannelWriter<WorldSnapshot> Outbound,
        PlayerSession? Session)
    {
        public OwnerState? LastOwner { get; set; }
    }

    public WorldServer(
        WorldDataHolder worldData,
        WorldCatalog? catalog = null,
        TimeProvider? clock = null,
        ISimRng? bossRng = null,
        FieldTerrainCatalog? terrain = null,
        WorldServerOptions? options = null)
    {
        _options = (options ?? WorldServerOptions.Default).Validated();
        _worldData = worldData;
        _terrain = terrain ?? new FieldTerrainCatalog();
        _catalog = catalog ?? new WorldCatalog();
        _clock = clock ?? TimeProvider.System;
        _clockOrigin = _clock.GetTimestamp();
        _bossSchedule = new BossSpawnSchedule(_clock.GetLocalNow(), bossRng ?? new CombatRng(0xB055));
    }

    private FieldInstance GetField(int fieldId)
    {
        if (_fields.TryGetValue(fieldId, out var field))
        {
            return field;
        }

        var data = _worldData.Current;
        var spawnData = data.FieldSpawns.TryGetValue(fieldId, out var spawns) ? spawns : FieldSpawnData.Empty;
        field = new FieldInstance(
            fieldId,
            spawnData,
            data.Monsters,
            _ids,
            new CombatRng(fieldId),
            data.Motions,
            data.Npcs,
            experienceMultiplier: _options.ExperienceMultiplier,
            items: data.Items,
            terrain: _terrain.Get(fieldId));
        _fields.Add(fieldId, field);
        return field;
    }

    public Task<bool> JoinAsync(int fieldId, WorldPlayer player, ChannelWriter<WorldSnapshot> outbound, PlayerSession? session = null)
    {
        return EnqueueAsync(player.EntityId, () =>
        {
            if (!_catalog.TryGetFieldByCode(fieldId, out var definition)
                || _connections.ContainsKey(player.EntityId)
                || session is not null && !session.IsPlaying)
            {
                return false;
            }

            var field = GetField(fieldId);
            var anchor = field.Terrain.PlaceOnFloor(RespawnPoint(definition, player.Position));
            player.EnterField(field.Terrain.PlaceOnFloor(player.Position), definition.State == FieldState.Village, anchor);
            field.Add(player);
            _connections.Add(player.EntityId, new Connection(fieldId, player, outbound, session));
            _membershipChanged = true;
            UpdatePlacement(session, fieldId, player.Position, definition.State == FieldState.Village);
            return true;
        });
    }

    // Leaving must not be refused: it waits for queue room and is exempt from the player's quota.
    public Task<bool> LeaveAsync(int fieldId, uint entityId, ChannelWriter<WorldSnapshot> outbound)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection) || !ReferenceEquals(connection.Outbound, outbound))
            {
                return false;
            }

            UpdatePlacement(connection.Session, connection.FieldId, connection.Player.Position);
            _fields[connection.FieldId].Remove(entityId);
            _connections.Remove(entityId);
            _pendingByPlayer.TryRemove(entityId, out _);
            _membershipChanged = true;
            return true;
        }, waitForCapacity: true);
    }

    public Task<bool> MoveToFieldAsync(
        int oldFieldId,
        int newFieldId,
        WorldPlayer player,
        Vector3 requestedPosition,
        ChannelWriter<WorldSnapshot> outbound)
    {
        return EnqueueAsync(player.EntityId, () =>
        {
            if (!WorldPosition.IsValid(requestedPosition)
                || player.IsDead
                || !_connections.TryGetValue(player.EntityId, out var connection)
                || connection.FieldId != oldFieldId
                || !ReferenceEquals(connection.Player, player)
                || !ReferenceEquals(connection.Outbound, outbound)
                || !_catalog.TryGetFieldByCode(oldFieldId, out var source)
                || !_catalog.TryGetFieldByCode(newFieldId, out var destination)
                || !FieldTransition.TryResolve(source, destination, player.Position, player.Level, out var arrival))
            {
                return false;
            }

            // The request's placement never becomes authoritative. Gates retain position; warps use catalog exits.
            _fields[oldFieldId].Remove(player.EntityId);
            var inVillage = destination.State == FieldState.Village;
            var terrain = GetField(newFieldId).Terrain;
            var respawnPoint = terrain.PlaceOnFloor(RespawnPoint(destination, arrival.Position));
            if (arrival.IsWarp)
            {
                // Warp exits carry an authored height; settle on the floor at or just below it rather than on a roof.
                player.EnterField(terrain.SettleNear(arrival.Position, FieldTerrain.DropTolerance * 2f), inVillage, respawnPoint);
            }
            else
            {
                player.CrossGate(inVillage, respawnPoint);
            }
            GetField(newFieldId).Add(player);
            _membershipChanged = true;
            _connections[player.EntityId] = connection with
            {
                FieldId = newFieldId
            };
            UpdatePlacement(connection.Session, newFieldId, arrival.Position, inVillage);
            return true;
        });
    }

    public Task<MovementVerdict> ReportMovementAsync(int fieldId, uint entityId, Vector3 position, float facing, bool moving, bool running, uint actionTarget)
    {
        return EnqueueAsync(entityId, () =>
        {
            if (_connections.TryGetValue(entityId, out var connection) && connection.FieldId == fieldId
                && _fields.TryGetValue(fieldId, out var field))
            {
                return connection.Player.ReportMovement(position, facing, moving, running, actionTarget, field.NowMs, field.Terrain);
            }

            return MovementVerdict.Ignored;
        });
    }

    public Task<Vector3?> PositionOfAsync(uint entityId)
    {
        return EnqueueAsync<Vector3?>(entityId, () => _connections.TryGetValue(entityId, out var connection) ? connection.Player.Position : null);
    }

    public Task<bool> SetAttackTargetAsync(int fieldId, uint attackerEntityId, uint targetEntityId, bool sustained, ulong sequence = 0)
    {
        return EnqueueAsync(attackerEntityId, () =>
        {
            return _fields.TryGetValue(fieldId, out var field) && field.SetAttackTarget(attackerEntityId, targetEntityId, sustained, sequence);
        });
    }

    public Task<CharacterSaveSnapshot?> CaptureForSaveAsync(PlayerSession session)
    {
        return EnqueueAsync<CharacterSaveSnapshot?>(NoOwner, () =>
        {
            if (session.Character is null || session.Loaded is null)
            {
                return null;
            }

            if (_connections.TryGetValue(session.EntityId, out var connection))
            {
                UpdatePlacement(session, connection.FieldId, connection.Player.Position);
            }

            return CharacterRecordFactory.Capture(session.Loaded, session.Character, session.FieldId, session.SpawnPosition);
        }, waitForCapacity: true);
    }

    public int PlayerCount => Volatile.Read(ref _playerCount);

    public Task<NpcInteraction> InteractNpcAsync(PlayerSession session, uint npcId)
    {
        return EnqueueAsync(session.EntityId, () =>
        {
            if (!session.IsPlaying || !_connections.TryGetValue(session.EntityId, out var connection)
                || !ReferenceEquals(connection.Session, session) || !_fields.TryGetValue(connection.FieldId, out var field)
                || !field.TryGetNpc(npcId, out var npc) || !ProgressionTransactions.CanReach(connection.Player, npc))
            {
                return NpcInteraction.Rejected("approach this NPC while alive");
            }

            var definition = npc.Definition;
            return new NpcInteraction(true, npc.EntityId, definition.Name,
                definition.Services, definition.Dialog, "");
        });
    }

    public Task<PreparedCharacterOperation<ProgressionOutcome>?> PrepareProgressionAsync(
        PlayerSession session, ProgressionCommand command)
    {
        return EnqueueAsync<PreparedCharacterOperation<ProgressionOutcome>?>(session.EntityId, () =>
        {
            if (!session.IsPlaying || session.Character is null || session.Loaded is null
                || !_connections.TryGetValue(session.EntityId, out var connection) || !ReferenceEquals(connection.Session, session))
            {
                return null;
            }

            return ProgressionTransactions.Prepare(session, connection.Player, _fields[connection.FieldId], command);
        });
    }

    public Task<bool> CommitProgressionAsync(PlayerSession session,
        PreparedCharacterOperation<ProgressionOutcome> operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (!ReferenceEquals(session.Character, operation.Original) || operation.Candidate is null
                || !operation.Original.ApplyProgression(operation.Candidate.ProgressionOperations[^1].Command))
            {
                throw new InvalidOperationException("Character ownership changed during progression commit.");
            }

            return true;
        }, waitForCapacity: true);
    }

    public Task<PreparedCharacterOperation<InventoryOutcome>?> PrepareInventoryAsync(PlayerSession session, InventoryCommand command)
    {
        return EnqueueAsync<PreparedCharacterOperation<InventoryOutcome>?>(session.EntityId, () =>
        {
            if (!session.IsPlaying || session.Character is null || session.Loaded is null
                || !_connections.TryGetValue(session.EntityId, out var connection) || !ReferenceEquals(connection.Session, session))
            {
                return null;
            }

            var original = session.Character;
            foreach (var applied in original.InventoryOperations)
            {
                if (applied.Command.OperationId == command.OperationId)
                {
                    var duplicate = applied.Command == command;
                    return new PreparedCharacterOperation<InventoryOutcome>(original, null, null,
                        new InventoryOutcome(duplicate, applied.Revision, duplicate ? "already committed" : "operation identity conflict"));
                }
            }

            if (command.Action == InventoryAction.Pickup)
            {
                return GroundPickupTransactions.Prepare(session, connection.Player, _fields[connection.FieldId], command);
            }

            if (command.Action == InventoryAction.ConsumePotion && (connection.Player.IsDead || connection.Player.HasPendingPotion))
            {
                return new PreparedCharacterOperation<InventoryOutcome>(original, null, null,
                    new InventoryOutcome(false, original.InventoryRevision, "cannot use another potion now"));
            }

            var candidate = original.CloneForInventoryOperation();
            if (!InventoryCommands.Apply(candidate, command))
            {
                return new PreparedCharacterOperation<InventoryOutcome>(original, null, null,
                    new InventoryOutcome(false, original.InventoryRevision, "invalid operation or stale inventory revision"));
            }

            candidate.RecordInventoryOperation(command);
            var snapshot = CharacterRecordFactory.Capture(session.Loaded, candidate, connection.FieldId, connection.Player.Position);
            var outcome = new InventoryOutcome(true, candidate.InventoryRevision, "committed");
            if (command.Action == InventoryAction.ConsumePotion)
            {
                var anchor = original.Inventory.Find(command.ItemId);
                return new PreparedPotionOperation(original, candidate, snapshot, outcome, original.Inventory[anchor]!.Definition.Potion!);
            }

            return new PreparedCharacterOperation<InventoryOutcome>(original, candidate, snapshot, outcome);
        });
    }

    public Task<bool> CommitInventoryAsync(PlayerSession session, PreparedCharacterOperation<InventoryOutcome> operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            // SaveGate remains held through commit, including when the connection leaves during storage I/O.
            if (!ReferenceEquals(session.Character, operation.Original) || operation.Candidate is null)
            {
                throw new InvalidOperationException("Character ownership changed during inventory commit.");
            }

            operation.Original.CommitInventoryOperation(operation.Candidate);
            if (operation is PreparedPickupOperation pickup)
            {
                if (!operation.Original.TryChangeMoney(pickup.Gold))
                {
                    throw new InvalidOperationException("Money changed beyond capacity during pickup commit.");
                }

                pickup.Ground.Resolve(pickup.DropId, pickup.OperationId, committed: true);
            }
            if (operation is PreparedPotionOperation potion && _connections.TryGetValue(session.EntityId, out var connection)
                && ReferenceEquals(connection.Session, session))
            {
                connection.Player.QueuePotion(potion.Potion);
            }

            return true;
        }, waitForCapacity: true);
    }

    public IReadOnlyList<(int FieldId, int PlayerCount)> ActiveFieldCounts()
    {
        return Volatile.Read(ref _activeCounts);
    }

    public Task<bool> AbortCharacterOperationAsync<T>(PreparedCharacterOperation<T> operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (operation is PreparedPickupOperation pickup)
            {
                pickup.Ground.Resolve(pickup.DropId, pickup.OperationId, committed: false);
            }

            return true;
        }, waitForCapacity: true);
    }

    private const uint NoOwner = 0;

    public int PendingCommandsOf(uint entityId) => _pendingByPlayer.TryGetValue(entityId, out var pending) ? Volatile.Read(ref pending.Count) : 0;

    // Not async: a refused command throws WorldBusyException to the caller synchronously instead of faulting the task.
    private Task<T> EnqueueAsync<T>(uint owner, Func<T> operation, bool waitForCapacity = false)
    {
        PendingCommands? pending = null;
        if (owner != NoOwner)
        {
            pending = _pendingByPlayer.GetOrAdd(owner, static _ => new PendingCommands());
            if (Interlocked.Increment(ref pending.Count) > _options.MaxPendingCommandsPerPlayer)
            {
                Interlocked.Decrement(ref pending.Count);
                throw new WorldBusyException("The player's command quota is full.");
            }
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action command = () =>
        {
            if (pending is not null)
            {
                Interlocked.Decrement(ref pending.Count);
            }

            try
            {
                completion.TrySetResult(operation());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        };
        if (_commands.Writer.TryWrite(command))
        {
            return completion.Task;
        }

        if (!waitForCapacity)
        {
            if (pending is not null)
            {
                Interlocked.Decrement(ref pending.Count);
            }

            throw new WorldBusyException("The authoritative command queue is full.");
        }

        // Durably admitted transactions cannot be abandoned when ordinary command admission is saturated.
        return WriteThenAwaitAsync(command, completion.Task);
    }

    private async Task<T> WriteThenAwaitAsync<T>(Action command, Task<T> result)
    {
        await _commands.Writer.WriteAsync(command);
        return await result;
    }

    public void Tick(float dt, uint serverTick)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        for (var processed = 0; processed < CommandsPerTick && _commands.Reader.TryRead(out var command); processed++)
        {
            command();
        }

        var localNow = _clock.GetLocalNow();
        bool bossPassDue = _bossSchedule.TryBeginPass(localNow, _clock.GetElapsedTime(_clockOrigin));
        int? bossHour = bossPassDue ? localNow.Hour : null;

        _activeFields.Clear();
        _activeFields.AddRange(_fields.Values);
        // Fields share only immutable catalogs and the thread-safe id allocator, so they step in parallel.
        Parallel.ForEach(_activeFields, field => StepField(field, dt, serverTick, bossHour));

        if (!_membershipChanged)
        {
            return;
        }

        _membershipChanged = false;
        Volatile.Write(ref _playerCount, _connections.Count);
        var counts = new List<(int, int)>();
        foreach (var field in _fields.Values)
        {
            if (field.PlayerCount > 0)
            {
                counts.Add((field.FieldId, field.PlayerCount));
            }
        }

        Volatile.Write(ref _activeCounts, counts.AsReadOnly());
    }

    private void StepField(FieldInstance field, float dt, uint serverTick, int? bossHour)
    {
        field.Step(dt, bossHour);
        if (field.PlayerCount == 0)
        {
            return;
        }

        var entities = field.BuildViews();
        var snapshot = new WorldSnapshot(serverTick, entities, field.SnapshotEvents(), field.FieldId, groundDrops: field.GroundLoot.Snapshot())
        {
            Grid = EntityGrid.Build(entities)
        };
        foreach (var entityId in field.PlayerIds)
        {
            if (_connections.TryGetValue(entityId, out var connection))
            {
                var owner = OwnerState.Capture(connection.Player.Character, connection.LastOwner);
                connection.LastOwner = owner;
                connection.Outbound.TryWrite(snapshot.WithOwner(owner));
            }
        }
    }

    private static Vector3 RespawnPoint(FieldDef field, Vector3 fallback)
    {
        if (field.StartPoints.Count == 0)
        {
            return fallback;
        }

        var point = field.StartPoints[0];
        return new Vector3(point.X, 0f, point.Z);
    }

    private static void UpdatePlacement(PlayerSession? session, int fieldId, Vector3 position, bool? inVillage = null)
    {
        if (session is null)
        {
            return;
        }

        session.FieldId = fieldId;
        session.SpawnPosition = position;
        if (inVillage.HasValue)
        {
            session.InVillage = inVillage.Value;
        }
    }
}
