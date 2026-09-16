using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PristonTale.Application.Sessions;
using PristonTale.Application.Services;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Application.World;
using PristonTale.Client.Net.Presence;

namespace PristonTale.Client.Net.Offline;

internal sealed class OfflinePlayChannel : IPlayChannel
{
    private readonly WorldServer _world;
    private readonly PlayerSession _session;
    private readonly WorldPlayer _player;
    private readonly Channel<WorldSnapshot> _outbound;
    private readonly SubscriberPresence _presence;
    private readonly OfflineWorldTick? _ownedTick;
    private readonly Task<bool> _joined;
    private readonly SessionAppService? _sessionApp;
    private Task? _disposeTask;
    private readonly object _disposeGate = new();

    public OfflinePlayChannel(WorldServer world, PlayerSession session, bool ownsTick = true, SessionAppService? sessionApp = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(session);
        if (session.Character is null || session.Loaded is null)
        {
            throw new InvalidOperationException("offline play channel: call EnterWorld before OpenPlayChannel");
        }

        _world = world;
        _session = session;
        _sessionApp = sessionApp;
        _player = WorldPlayerFactory.Create(session);
        _outbound = new WorldOutbox();
        if (!session.TryBeginPlay())
        {
            throw new InvalidOperationException("session already playing or closing");
        }

        _joined = _world.JoinAsync(session.FieldId, _player, _outbound.Writer, session);
        _presence = new SubscriberPresence(session.EntityId);
        if (ownsTick)
        {
            _ownedTick = new OfflineWorldTick(_world);
        }
    }

    public async Task SendMoveAsync(Vector3 target, bool run, CancellationToken ct = default)
    {
        await EnsureJoinedAsync();
        await _world.SetMoveTargetAsync(_session.FieldId, _session.EntityId, target, run);
    }

    public Task SendHeartbeatAsync(uint clientTick, CancellationToken ct = default) => Task.CompletedTask;

    public async Task SendEnterFieldAsync(int fieldCode, Vector3 position, CancellationToken ct = default)
    {
        await EnsureJoinedAsync();
        await _world.MoveToFieldAsync(_session.FieldId, fieldCode, _player, position, _outbound.Writer);
    }

    public async Task SendAttackAsync(uint targetEntityId, bool sustained, CancellationToken ct = default, ulong sequence = 0)
    {
        await EnsureJoinedAsync();
        await _world.SetAttackTargetAsync(_session.FieldId, _session.EntityId, targetEntityId, sustained, sequence);
    }

    public async IAsyncEnumerable<PresenceUpdate> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await EnsureJoinedAsync();
        PristonTale.Simulation.Characters.OwnerState? lastOwner = null;
        await foreach (var snapshot in _outbound.Reader.ReadAllAsync(ct))
        {
            var diff = _presence.Diff(snapshot);
            var changedOwner = snapshot.Owner != lastOwner ? snapshot.Owner : null;
            lastOwner = snapshot.Owner;
            yield return OfflinePresenceMapping.ToNeutral(snapshot.ServerTick, diff, snapshot.FieldId, changedOwner);
        }
    }

    private async Task EnsureJoinedAsync()
    {
        if (!await _joined)
        {
            throw new InvalidOperationException("offline world join rejected");
        }
    }

    public async Task<NpcInteraction> InteractNpcAsync(uint entityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureJoinedAsync();
        return await _world.InteractNpcAsync(_session, entityId);
    }

    public async Task<ProgressionOutcome> ApplyProgressionAsync(ProgressionCommand command, CancellationToken ct = default)
    {
        await EnsureJoinedAsync();
        return _sessionApp is not null ? await _sessionApp.ApplyProgressionAsync(_session.SessionToken, command, ct)
            : new ProgressionOutcome(false, 0, "progression service unavailable");
    }

    public async Task<InventoryOutcome> ApplyInventoryAsync(InventoryCommand command, CancellationToken ct = default)
    {
        await EnsureJoinedAsync();
        return _sessionApp is not null ? await _sessionApp.ApplyInventoryAsync(_session.SessionToken, command, ct)
            : new InventoryOutcome(false, 0, "inventory service unavailable");
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            if (await _joined)
            {
                // Leave while the tick still runs: it snapshots placement and ends all character mutation.
                await _world.LeaveAsync(_session.FieldId, _session.EntityId, _outbound.Writer);
            }
        }
        finally
        {
            _session.EndPlay();
            if (_ownedTick is not null)
            {
                await _ownedTick.DisposeAsync();
            }

            _outbound.Writer.TryComplete();
        }
    }
}
