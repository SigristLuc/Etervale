using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Effects.Rendering;

public enum ParticleBlendMode
{
    InverseShadow = 2,
    ColorBlend = 3,
    Additive = 0,

    AlphaBlend = 1
}

public sealed class ParticleBlendStates : IDisposable
{
    private readonly BlendState InverseShadow = new()
    {
        ColorSourceBlend = Blend.Zero,
        ColorDestinationBlend = Blend.InverseSourceColor,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One
    };
    private readonly BlendState ColorBlend = new()
    {
        ColorSourceBlend = Blend.SourceColor,
        ColorDestinationBlend = Blend.InverseSourceColor,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One
    };
    public BlendState Get(ParticleBlendMode mode) => mode switch
    {
        ParticleBlendMode.Additive => BlendState.Additive,
        ParticleBlendMode.AlphaBlend => BlendState.NonPremultiplied,
        ParticleBlendMode.InverseShadow => InverseShadow,
        ParticleBlendMode.ColorBlend => ColorBlend,
        _ => BlendState.Additive
    };

    public void Dispose()
    {
        InverseShadow.Dispose();
        ColorBlend.Dispose();
    }
}
