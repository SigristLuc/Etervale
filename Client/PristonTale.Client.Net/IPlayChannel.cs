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

    // TRANSPLAYDATA: the client's own position (PT-global coordinates), motion and action target, plus its field code.
    Task SendMovementAsync(Vector3 position, float facing, bool moving, bool running, uint actionTarget, int fieldCode, CancellationToken ct = default);

    Task SendAttackAsync(uint targetEntityId, bool sustained, CancellationToken ct = default, ulong sequence = 0);

    IAsyncEnumerable<PresenceUpdate> ReadAllAsync(CancellationToken ct = default);
}
