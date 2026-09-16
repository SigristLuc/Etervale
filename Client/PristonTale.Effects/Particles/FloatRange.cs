namespace PristonTale.Effects.Particles;

public readonly record struct FloatRange(float Min, float Max)
{
    public float Sample(float t01)
    {
        var t = t01 < 0f ? 0f : (t01 > 1f ? 1f : t01);
        return Min + (Max - Min) * t;
    }

    public static FloatRange Constant(float value) => new(value, value);
}
