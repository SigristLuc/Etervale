using Microsoft.Xna.Framework;

namespace PristonTale.Effects.Particles;

public readonly record struct SizeOverLife(float Start, float End)
{
    public float At(float life01)
    {
        var t = life01 < 0f ? 0f : (life01 > 1f ? 1f : life01);
        return MathHelper.Lerp(Start, End, t);
    }

    public static SizeOverLife Constant(float size) => new(size, size);
}
