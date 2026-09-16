using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Particles;

namespace PristonTale.Effects.Rendering;

public readonly ref struct EmitterRenderView
{
    public readonly ReadOnlySpan<Particle> Particles;

    public readonly EmitterDefinition Definition;

    public readonly Texture2D? Texture;

    public readonly ParticleBlendMode Blend;

    public EmitterRenderView(ReadOnlySpan<Particle> particles, EmitterDefinition definition, Texture2D? texture, ParticleBlendMode blend)
    {
        Particles = particles;
        Definition = definition;
        Texture = texture;
        Blend = blend;
    }
}
