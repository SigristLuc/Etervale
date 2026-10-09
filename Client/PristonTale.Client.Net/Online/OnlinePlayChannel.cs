using System.Numerics;
using System.Runtime.CompilerServices;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Client.Net.Online;

// Opening sends the client's PLAYERINFO and completes on CONNECTED; disposing sends the Etervale leave request so the
// server saves before the client returns to another screen.
internal sealed class OnlinePlayChannel : IPlayChannel
{
    private readonly GameServerConnection _connection;
    private readonly CancellationTokenSource _lifetime;
    private readonly Task _start;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;

    public OnlinePlayChannel(GameServerConnection connection, CancellationToken cancellationToken = default)
    {
        _connection = connection;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _start = connection.OpenPlayAsync(_lifetime.Token);
    }

    public async Task<ProgressionOutcome> ApplyProgressionAsync(ProgressionCommand command, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.ApplyProgressionAsync(command, ct);
    }

    public async Task<NpcInteraction> InteractNpcAsync(uint entityId, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.InteractNpcAsync(entityId, ct);
    }

    public async Task<InventoryOutcome> ApplyInventoryAsync(InventoryCommand command, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.ApplyInventoryAsync(command, ct);
    }

    public async Task SendMovementAsync(Vector3 position, float facing, bool moving, bool running, uint actionTarget, int fieldCode, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendMovement(position, facing, moving, running, actionTarget, fieldCode);
    }

    public async Task SendAttackAsync(uint targetEntityId, bool sustained, CancellationToken ct = default, ulong sequence = 0)
    {
        await _start.WaitAsync(ct);
        _connection.SendAttackPacket(targetEntityId, sustained ? 1 : 0, sequence);
    }

    public async IAsyncEnumerable<PresenceUpdate> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        await foreach (var update in _connection.ReadPresenceAsync(linked.Token))
        {
            yield return update;
        }
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
            if (!_start.IsCompleted)
            {
                await _lifetime.CancelAsync();
            }

            try
            {
                await _start;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }

            if (_start.IsCompletedSuccessfully)
            {
                await _connection.ClosePlayAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            await _lifetime.CancelAsync();
            _lifetime.Dispose();
        }
    }
}
