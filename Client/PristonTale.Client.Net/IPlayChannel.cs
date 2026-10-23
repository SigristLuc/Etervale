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

    // One typed NPC service request; the reply carries the shop, warehouse or teleport state the server sent with it.
    Task<NpcServiceReply> RequestNpcServiceAsync(NpcServiceCommand command, CancellationToken ct = default);
    Task<InventoryOutcome> ApplyInventoryAsync(InventoryCommand command, CancellationToken ct = default);

    // TRANSPLAYDATA: the client's own position (PT-global coordinates), motion and action target, plus its field code.
    Task SendMovementAsync(Vector3 position, float facing, bool moving, bool running, uint actionTarget, int fieldCode, CancellationToken ct = default);

    Task SendAttackAsync(uint targetEntityId, bool sustained, CancellationToken ct = default, ulong sequence = 0);

    // SkillCast: the server answers with a SkillCastResult in the presence stream (PresenceUpdate.CastResult), correlated
    // by sequence. point is the aimed ground position in PT-global coordinates, or null for an entity or self cast.
    Task SendSkillCastAsync(uint sequence, uint skillCode, uint targetEntityId, Vector3? point, CancellationToken ct = default);

    // A command about a running skill (SkillCastFlags: the release of a held charge, an order to the summon).
    Task SendSkillCommandAsync(uint sequence, uint skillCode, ushort command, CancellationToken ct = default) => Task.CompletedTask;

    IAsyncEnumerable<PresenceUpdate> ReadAllAsync(CancellationToken ct = default);
}
