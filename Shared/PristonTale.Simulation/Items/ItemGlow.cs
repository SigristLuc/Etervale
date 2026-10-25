namespace PristonTale.Simulation.Items;

// The glow of a unique weapon: colour channels 0..255, the blink period in 70 Hz ticks and the scale of its pulse.
public readonly record struct ItemGlow(byte R, byte G, byte B, byte A, int Blink, float Scale)
{
    public static readonly ItemGlow None = default;

    public bool Visible => R != 0 || G != 0 || B != 0 || A != 0;
}
