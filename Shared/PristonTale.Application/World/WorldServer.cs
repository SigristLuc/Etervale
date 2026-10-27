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
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Contracts.Skills;

namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    public const int TickHz = 20;

    private readonly Channel<WorldCommand> _commands = Channel.CreateBounded<WorldCommand>(new BoundedChannelOptions(65536)
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
    private readonly Dictionary<ulong, Connection> _connections = new();
    private readonly EntityIdAllocator _ids = new();
    private readonly WorldDataHolder _worldData;
    private readonly WorldCatalog _catalog;
    private readonly TimeProvider _clock;
    private readonly long _clockOrigin;
    private readonly BossSpawnSchedule _bossSchedule;
    private readonly FieldTerrainCatalog _terrain;
    private WorldServerOptions _options;
    // Commands each player has waiting in the shared queue; entries are dropped when the player leaves the world.
    private readonly ConcurrentDictionary<ulong, PendingCommands> _pendingByPlayer = new();

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

        public long CooldownRevision { get; set; } = -1;
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
        ConfigureQuests(field);
        field.State = _catalog.TryGetFieldByCode(fieldId, out var definition) ? definition.State : FieldState.Forest;
        ApplyOptions(field, _options);
        field.Event = _catalog.TryGetFieldByCode(fieldId, out var described) ? described.FieldEvent : FieldEventKind.None;
        field.DropLedger = DropLedgerFor(_worldData.Current.Items);
        _fields.Add(fieldId, field);
        return field;
    }

    public WorldServerOptions Options => Volatile.Read(ref _options);

    public Task ApplyOptionsAsync(WorldServerOptions options)
    {
        var validated = options.Validated();
        return EnqueueAsync(NoOwner, () =>
        {
            Volatile.Write(ref _options, validated);
            foreach (var field in _fields.Values)
            {
                ApplyOptions(field, validated);
            }

            return true;
        }, waitForCapacity: true);
    }

    private static void ApplyOptions(FieldInstance field, WorldServerOptions options)
    {
        field.ItemEventRates = options.ItemEvents;
        field.ExperienceMultiplier = options.ExperienceMultiplier;
        field.ExperienceEventPercent = options.ExperienceEventPercent;
        field.Aging = options.Aging;
        field.Mixing = options.Mixing;
    }

    // The game clock in game milliseconds: the Unix time plus the configured offset (World:ClockOffsetMinutes game minutes).
    public long GameClockMs => _clock.GetUtcNow().ToUnixTimeMilliseconds() + _options.ClockOffsetMinutes * (long)WorldClock.MinuteMs;

    private DailyDropLedger? _dropLedger;

    // One ledger per item catalog: a reloaded world data starts its own count.
    private DailyDropLedger DropLedgerFor(ItemCatalog items) =>
        _dropLedger is { } ledger && ReferenceEquals(_dropLedgerItems, items) ? ledger : (_dropLedger = new DailyDropLedger(_dropLedgerItems = items, _clock));

    private ItemCatalog? _dropLedgerItems;

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
            // A saved place without a floor (outside the stage) enters at the field's first start point instead.
            if (!field.Terrain.TryPlaceOnFloor(player.Position, out var position))
            {
                var start = definition.StartPoints.Count > 0 ? definition.StartPoints[0] : definition.Center;
                position = field.Terrain.PlaceOnFloor(new Vector3(start.X, 0f, start.Z));
            }

            player.EnterField(position, definition.State == FieldState.Village);
            field.Add(player);
            _connections.Add(player.EntityId, new Connection(fieldId, player, outbound, session));
            _membershipChanged = true;
            UpdatePlacement(session, fieldId, player.Position, definition.State == FieldState.Village);
            return true;
        });
    }

    // Leaving must not be refused: it waits for queue room and is exempt from the player's quota.
    // The character is captured in the same tick it leaves, so a player who leaves dead is stored at the town restart.
    public Task<WorldLeave> LeaveAsync(int fieldId, ulong entityId, ChannelWriter<WorldSnapshot> outbound)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection) || !ReferenceEquals(connection.Outbound, outbound))
            {
                return new WorldLeave(false, null);
            }

            var save = connection.Session is { } session ? CaptureForSave(session, connection) : null;
            _fields[connection.FieldId].Remove(entityId);
            _connections.Remove(entityId);
            _pendingByPlayer.TryRemove(entityId, out _);
            _membershipChanged = true;
            return new WorldLeave(true, save);
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
            // Reports keep naming the new field until the client hears the move committed: an already-made move is a success,
            // not a refusal the session would answer with a forced warp.
            if (_connections.TryGetValue(player.EntityId, out var current) && ReferenceEquals(current.Outbound, outbound)
                && current.FieldId == newFieldId && oldFieldId != newFieldId)
            {
                return true;
            }

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
            TransferPlayer(player, _fields[oldFieldId], GetField(newFieldId));
            var inVillage = destination.State == FieldState.Village;
            var terrain = GetField(newFieldId).Terrain;
            if (arrival.IsWarp)
            {
                // Warp exits carry an authored height; settle on the floor at or just below it rather than on a roof.
                player.EnterField(terrain.SettleNear(arrival.Position, FieldTerrain.DropTolerance * 2f), inVillage);
            }
            else
            {
                player.CrossGate(inVillage);
            }
            _membershipChanged = true;
            _connections[player.EntityId] = connection with
            {
                FieldId = newFieldId
            };
            UpdatePlacement(connection.Session, newFieldId, arrival.Position, inVillage);
            return true;
        });
    }

    public Task<MovementVerdict> ReportMovementAsync(int fieldId, ulong entityId, Vector3 position, float facing, bool moving, bool running, ulong actionTarget)
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

    public Task<SkillCastOutcome> CastSkillAsync(int fieldId, ulong entityId, SkillCastRequest request)
    {
        return EnqueueAsync(entityId, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection) || connection.FieldId != fieldId
                || !_fields.TryGetValue(fieldId, out var field))
            {
                return SkillCastOutcome.Refused(request, SkillCastVerdict.Stale);
            }

            return field.CastSkill(connection.Player, request);
        });
    }

    public Task<Vector3?> PositionOfAsync(ulong entityId)
    {
        return EnqueueAsync<Vector3?>(entityId, () => _connections.TryGetValue(entityId, out var connection) ? connection.Player.Position : null);
    }

    public Task<bool> SetAttackTargetAsync(int fieldId, ulong attackerEntityId, ulong targetEntityId, bool sustained, ulong sequence = 0)
    {
        return EnqueueAsync(attackerEntityId, () =>
        {
            return _fields.TryGetValue(fieldId, out var field) && field.SetAttackTarget(attackerEntityId, targetEntityId, sustained, sequence);
        });
    }

    public Task<CharacterSaveSnapshot?> CaptureForSaveAsync(PlayerSession session)
    {
        return EnqueueAsync(NoOwner, () => CaptureForSave(session, _connections.GetValueOrDefault(session.EntityId)), waitForCapacity: true);
    }

    private CharacterSaveSnapshot? CaptureForSave(PlayerSession session, Connection? connection)
    {
        if (session.Character is null || session.Loaded is null)
        {
            return null;
        }

        if (connection is not null)
        {
            if (connection.Player.IsDead && CaptureDead(session, connection) is { } dead)
            {
                return dead;
            }

            UpdatePlacement(session, connection.FieldId, connection.Player.Position);
        }

        return CharacterRecordFactory.Capture(session.Loaded, session.Character, session.FieldId, session.SpawnPosition);
    }

    public int PlayerCount => Volatile.Read(ref _playerCount);

    // Tick-thread state: callers must not run concurrently with Tick.
    // Tick only. The session's connection while the session owns it, which a commit or an abort still needs after the
    // player started leaving.
    private bool TryGetOwnConnection(PlayerSession session, out Connection connection) =>
        _connections.TryGetValue(session.EntityId, out connection!) && ReferenceEquals(connection.Session, session);

    // Tick only. The session's connection while its character plays in the world.
    private bool TryGetPlaying(PlayerSession session, out Connection connection) =>
        TryGetOwnConnection(session, out connection) && session.IsPlaying && session.Character is not null;

    internal long FieldNowMs(int fieldId) => _fields.TryGetValue(fieldId, out var field) ? field.NowMs : 0;

    internal FieldInstance? FieldFor(int fieldId) => _fields.GetValueOrDefault(fieldId);

    public Task<PreparedCharacterOperation<ProgressionOutcome>?> PrepareProgressionAsync(
        PlayerSession session, ProgressionCommand command)
    {
        return EnqueueAsync<PreparedCharacterOperation<ProgressionOutcome>?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection))
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
            var moneyBefore = operation.Original.Money;
            if (!ReferenceEquals(session.Character, operation.Original) || operation.Candidate is null
                || !operation.Original.ApplyProgression(operation.Candidate.ProgressionOperations[^1].Command))
            {
                throw new CommitInvariantException("Character ownership changed during progression commit.");
            }

            Currency.Record(GameMasters.CurrencySink.SkillTraining, moneyBefore - operation.Original.Money);

            return true;
        }, waitForCapacity: true);
    }

    public Task<PreparedCharacterOperation<InventoryOutcome>?> PrepareInventoryAsync(PlayerSession session, InventoryCommand command)
    {
        return EnqueueAsync<PreparedCharacterOperation<InventoryOutcome>?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection))
            {
                return null;
            }

            var original = connection.Player.Character;
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
                    new InventoryOutcome(false, original.InventoryRevision,
                        InventoryCommands.Refusal(original, command) ?? "invalid operation or stale inventory revision"));
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
                throw new CommitInvariantException("Character ownership changed during inventory commit.");
            }

            operation.Original.CommitInventoryOperation(operation.Candidate);
            if (operation is PreparedPickupOperation pickup)
            {
                if (!operation.Original.TryChangeMoney(pickup.Gold))
                {
                    throw new CommitInvariantException("Money changed beyond capacity during pickup commit.");
                }

                pickup.Ground.Resolve(pickup.DropId, pickup.OperationId, committed: true);
            }
            if (operation is PreparedPotionOperation potion && TryGetOwnConnection(session, out var connection))
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

    private const ulong NoOwner = 0;

    public int PendingCommandsOf(ulong entityId) => _pendingByPlayer.TryGetValue(entityId, out var pending) ? Volatile.Read(ref pending.Count) : 0;

    // Not async: a refused command throws WorldBusyException to the caller synchronously instead of faulting the task.
    private Task<T> EnqueueAsync<T>(ulong owner, Func<T> operation, bool waitForCapacity = false)
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
        if (_commands.Writer.TryWrite(new WorldCommand(command, default)))
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
        await _commands.Writer.WriteAsync(new WorldCommand(command, default));
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
            if (command.Action is { } action)
            {
                action();
            }
            else
            {
                ApplyMovement(command.Movement);
            }
        }

        var localNow = _clock.GetLocalNow();
        bool bossPassDue = _bossSchedule.TryBeginPass(localNow, _clock.GetElapsedTime(_clockOrigin));
        int? bossHour = bossPassDue ? localNow.Hour : null;

        _activeFields.Clear();
        _activeFields.AddRange(_fields.Values);
        var nowSeconds = _clock.GetUtcNow().ToUnixTimeSeconds();
        // Fields share only immutable catalogs and the thread-safe id allocator, so they step in parallel.
        var night = WorldClock.IsNight(WorldClock.HoursAt(GameClockMs));
        Parallel.ForEach(_activeFields, field => StepField(field, dt, serverTick, bossHour, nowSeconds, night));
        ApplyQuestTravels();

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

    private void StepField(FieldInstance field, float dt, uint serverTick, int? bossHour, long nowSeconds, bool night)
    {
        field.Premium.NowSeconds = nowSeconds;
        field.Night = night;
        field.Step(dt, bossHour);
        if (field.PlayerCount == 0)
        {
            return;
        }

        var frame = field.Frames.Rent(field.EntityCount);
        field.FillViews(frame.Views);
        frame.Grid.Rebuild(frame.Views);
        // Open to readers before any outbox can hand it out.
        field.Frames.Published(frame);
        var snapshot = new WorldSnapshot(serverTick, frame.Views, field.SnapshotEvents(), field.FieldId, groundDrops: field.GroundLoot.Snapshot())
        {
            Grid = frame.Grid,
            Frame = frame,
            FrameGeneration = frame.Generation,
            SkillEvents = field.SnapshotSkillEvents(),
            StatusEvents = field.SnapshotStatusEvents(),
            ItemEffects = field.ItemEvents.Snapshot(),
            ActiveStatuses = field.Statuses.Snapshot,
            FieldNowMs = field.NowMs
        };
        frame.Routes.Rebuild(snapshot.Events, snapshot.SkillEvents, snapshot.StatusEvents, snapshot.ItemEffects);
        foreach (var entityId in field.PlayerIds)
        {
            if (_connections.TryGetValue(entityId, out var connection))
            {
                var owner = OwnerState.Capture(connection.Player.Character, connection.LastOwner, nowSeconds);
                connection.LastOwner = owner;
                var cooldowns = connection.Player.Cooldowns.SnapshotIfChanged(connection.CooldownRevision, field.NowMs);
                connection.CooldownRevision = connection.Player.Cooldowns.Revision;
                connection.Outbound.TryWrite(snapshot.WithOwner(owner, cooldowns) with
                {
                    Quest = connection.Player.QuestState.TakeUpdate(connection.Player.Character),
                    Restart = connection.Player.IsDead ? DeathPenalty.Offer(connection.Player.Character, field.State) : null
                });
                // Only an outbox pins and releases frames; any other writer may keep the snapshot forever.
                frame.Retained |= connection.Outbound is not WorldOutbox.OutboxWriter;
            }
        }
    }

    // Moves a player between field instances on the command phase: its cooldowns and running statuses follow it onto the
    // destination's clock.
    private static void TransferPlayer(WorldPlayer player, FieldInstance from, FieldInstance to)
    {
        var statuses = from.Statuses.Take(player.EntityId);
        from.Remove(player.EntityId);
        player.Cooldowns.Rebase(from.NowMs, to.NowMs);
        to.Add(player);
        to.Statuses.Carry(player.EntityId, statuses, from.NowMs, to.NowMs);
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
