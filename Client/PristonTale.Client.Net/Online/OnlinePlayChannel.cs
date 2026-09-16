using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Grpc.Core;
using PristonTale.Client.Net.Presence;
using PristonTale.Protocol.Common;
using PristonTale.Protocol.Session;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Client.Net.Online;

internal sealed class OnlinePlayChannel : IPlayChannel
{
    private readonly AsyncDuplexStreamingCall<ClientInput, WorldUpdate> _call;
    private readonly Channel<PendingWrite> _outgoing = Channel.CreateBounded<PendingWrite>(
        new BoundedChannelOptions(128)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly Task _writer;
    private readonly Func<InventoryCommand, CancellationToken, Task<InventoryOutcome>>? _inventory;
    private readonly Func<ProgressionCommand, CancellationToken, Task<ProgressionOutcome>>? _progression;
    private readonly Func<uint, CancellationToken, Task<NpcInteraction>>? _npc;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;

    private sealed record PendingWrite(ClientInput Input, CancellationToken Cancellation, TaskCompletionSource Completion);

    public OnlinePlayChannel(AsyncDuplexStreamingCall<ClientInput, WorldUpdate> call,
        Func<InventoryCommand, CancellationToken, Task<InventoryOutcome>>? inventory = null,
        Func<ProgressionCommand, CancellationToken, Task<ProgressionOutcome>>? progression = null,
        Func<uint, CancellationToken, Task<NpcInteraction>>? npc = null)
    {
        _call = call;
        _inventory = inventory;
        _progression = progression;
        _npc = npc;
        _writer = WriteLoopAsync();
    }

    public Task<ProgressionOutcome> ApplyProgressionAsync(ProgressionCommand command, CancellationToken ct = default)
    {
        return _progression is not null ? _progression(command, ct)
            : Task.FromResult(new ProgressionOutcome(false, 0, "progression transport unavailable"));
    }

    public Task<NpcInteraction> InteractNpcAsync(uint entityId, CancellationToken ct = default)
    {
        return _npc is not null ? _npc(entityId, ct) : Task.FromResult(NpcInteraction.Rejected("NPC transport unavailable"));
    }

    public Task SendMoveAsync(Vector3 target, bool run, CancellationToken ct = default)
    {
        return SendAsync(new ClientInput { Move = new MoveIntent { Target = new Vec3 { X = target.X, Y = target.Y, Z = target.Z }, Run = run } }, ct);
    }

    public Task SendHeartbeatAsync(uint clientTick, CancellationToken ct = default)
    {
        return SendAsync(new ClientInput { Heartbeat = new Heartbeat { ClientTick = clientTick } }, ct);
    }

    public Task SendEnterFieldAsync(int fieldCode, Vector3 position, CancellationToken ct = default)
    {
        return SendAsync(
            new ClientInput { EnterField = new EnterFieldIntent { FieldCode = fieldCode, Position = new Vec3 { X = position.X, Y = position.Y, Z = position.Z } } },
            ct);
    }

    public Task SendAttackAsync(uint targetEntityId, bool sustained, CancellationToken ct = default, ulong sequence = 0)
    {
        return SendAsync(new ClientInput { Attack = new AttackIntent { TargetEntityId = targetEntityId, Sustained = sustained, Sequence = sequence } }, ct);
    }

    private async Task SendAsync(ClientInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_outgoing.Writer.TryWrite(new PendingWrite(input, ct, completion)))
        {
            throw new InvalidOperationException("The outgoing gameplay queue is full or closed.");
        }

        await completion.Task;
    }

    public Task<InventoryOutcome> ApplyInventoryAsync(InventoryCommand command, CancellationToken ct = default)
    {
        return _inventory is not null ? _inventory(command, ct)
            : Task.FromResult(new InventoryOutcome(false, 0, "inventory transport unavailable"));
    }

    private async Task WriteLoopAsync()
    {
        Exception? failure = null;
        try
        {
            await foreach (var pending in _outgoing.Reader.ReadAllAsync())
            {
                if (pending.Cancellation.IsCancellationRequested)
                {
                    pending.Completion.TrySetCanceled(pending.Cancellation);
                    continue;
                }

                try
                {
                    await _call.RequestStream.WriteAsync(pending.Input, pending.Cancellation);
                    pending.Completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    pending.Completion.TrySetException(ex);
                    throw;
                }
            }

            await _call.RequestStream.CompleteAsync();
        }
        catch (Exception ex)
        {
            failure = ex;
            _outgoing.Writer.TryComplete(ex);
            throw;
        }
        finally
        {
            while (_outgoing.Reader.TryRead(out var pending))
            {
                pending.Completion.TrySetException(failure ?? new ObjectDisposedException(nameof(OnlinePlayChannel)));
            }
        }
    }

    public async IAsyncEnumerable<PresenceUpdate> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var update in _call.ResponseStream.ReadAllAsync(ct))
        {
            yield return PresenceMapping.ToNeutral(update);
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
        _outgoing.Writer.TryComplete();
        try
        {
            // Drain accepted writes before half-closing. A stalled peer cannot hold shutdown forever.
            await _writer.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            _call.Dispose();
            _ = ObserveWriterAsync();
        }
    }

    private async Task ObserveWriterAsync()
    {
        try
        {
            await _writer;
        }
        catch
        {
            // The active send and DisposeAsync already report failure; observe late cancellation too.
        }
    }
}
