using System.Numerics;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Client.Net;

public interface IPlayChannel : IAsyncDisposable
{
    Task<ProgressionOutcome> ApplyProgressionAsync(ProgressionCommand command, CancellationToken ct = default);
    Task<NpcInteraction> InteractNpcAsync(uint entityId, CancellationToken ct = default);
    Task<InventoryOutcome> ApplyInventoryAsync(InventoryCommand command, CancellationToken ct = default);
    Task SendMoveAsync(Vector3 target, bool run, CancellationToken ct = default);

    Task SendHeartbeatAsync(uint clientTick, CancellationToken ct = default);

    Task SendEnterFieldAsync(int fieldCode, Vector3 position, CancellationToken ct = default);

    Task SendAttackAsync(uint targetEntityId, bool sustained, CancellationToken ct = default, ulong sequence = 0);

    IAsyncEnumerable<PresenceUpdate> ReadAllAsync(CancellationToken ct = default);
}
