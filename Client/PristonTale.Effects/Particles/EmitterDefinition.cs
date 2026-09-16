using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Rendering;

namespace PristonTale.Effects.Particles;

public sealed record EmitterDefinition
{
    public int MaxParticles { get; init; } = 256;

    public float EmissionRate { get; init; }

    public int BurstCount { get; init; }

    public FloatRange Lifetime { get; init; } = FloatRange.Constant(1f);

    public FloatRange InitialSpeed { get; init; } = FloatRange.Constant(0f);

    public Vector3 EmitDirection { get; init; } = Vector3.Up;

    public float DirectionSpreadRadians { get; init; }

    public Vector3 Gravity { get; init; } = Vector3.Zero;

    public float Damping { get; init; }

    public FloatRange SpawnRadius { get; init; } = FloatRange.Constant(0f);

    public ColorOverLife Color { get; init; } = ColorOverLife.Constant(Microsoft.Xna.Framework.Color.White);

    public SizeOverLife Size { get; init; } = SizeOverLife.Constant(1f);

    public FloatRange InitialRotation { get; init; } = FloatRange.Constant(0f);

    public FloatRange RotationSpeed { get; init; } = FloatRange.Constant(0f);

    public ParticleBlendMode Blend { get; init; } = ParticleBlendMode.Additive;

    public IReadOnlyList<Texture2D> Frames { get; init; } = [];

    public float FramesPerSecond { get; init; }

    public bool LoopFrames { get; init; } = true;

    public bool FrameByLifetime { get; init; }

    public ParticleAnimation? Animation { get; init; }
    public bool PotionMotion { get; init; }
    public PristonTale.Effects.Legacy.LegacyParticleSequence? LegacySequence { get; init; }
}
