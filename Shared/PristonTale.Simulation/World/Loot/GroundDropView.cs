using System.Numerics;

namespace PristonTale.Simulation.World.Loot;

public sealed record GroundDropView(Guid Id, uint ItemCode, string CatalogKey, string Name, int Amount, bool Gold, Vector3 Position);

public sealed record GroundDropState(GroundDropView View, uint OwnerId, bool Public);
