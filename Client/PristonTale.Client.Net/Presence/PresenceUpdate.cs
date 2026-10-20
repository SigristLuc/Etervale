using PristonTale.Simulation.Characters;

namespace PristonTale.Client.Net.Presence;

public sealed record PresenceUpdate(
    long ServerTick,
    IReadOnlyList<EntitySpawnInfo> Spawns,
    IReadOnlyList<EntityStateInfo> States,
    IReadOnlyList<uint> Removed,
    IReadOnlyList<CombatEventInfo> Events,
    int? FieldId = null,
    OwnerState? Owner = null)
{
    public IReadOnlyList<PristonTale.Simulation.World.Loot.GroundDropView> GroundAdded { get; init; } = [];
    public IReadOnlyList<Guid> GroundRemoved { get; init; } = [];

    // WARPFIELD for the local player: a forced correction after a rejected report, otherwise a field confirmation.
    public WarpInfo? Warp { get; init; }

    // Server simulation time of a freshly received group or event list (PresenceClock extension); null otherwise.
    public double? ServerTimeMs { get; init; }
}

public sealed record WarpInfo(int FieldId, System.Numerics.Vector3 Position, bool Forced);
