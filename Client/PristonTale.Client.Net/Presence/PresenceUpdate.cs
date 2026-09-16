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
}
