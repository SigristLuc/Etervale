using Microsoft.Xna.Framework;

namespace PristonTale.Effects.Particles;

public readonly record struct ColorOverLife(Color Start, Color End)
{
    public Color At(float life01)
    {
        var t = life01 < 0f ? 0f : (life01 > 1f ? 1f : life01);
        return Color.Lerp(Start, End, t);
    }

    public static ColorOverLife Constant(Color color) => new(color, color);
}
