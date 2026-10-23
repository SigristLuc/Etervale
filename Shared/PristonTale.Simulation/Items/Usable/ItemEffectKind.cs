namespace PristonTale.Simulation.Items.Usable;

// The visible effect an item use plays around its user for everyone nearby. Wire values; append only.
public enum ItemEffectKind : byte
{
    None = 0,
    Firecracker = 1,
    Mastery = 2,
    ForceOrb = 3,
    Learn = 4
}
