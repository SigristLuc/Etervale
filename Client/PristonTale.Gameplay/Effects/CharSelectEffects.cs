using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Effects;
using PristonTale.Effects.Particles;
using PristonTale.Effects.Rendering;

namespace PristonTale.Gameplay.Effects;

public static class CharSelectEffects
{
    public const string GreenBeamName = "charselect_green_beam";

    private static readonly Color BeamColorStart = new(80, 235, 110, 115);
    private static readonly Color BeamColorEnd = new(10, 55, 22, 0);

    private const float BeamSizeStart = 12f;
    private const float BeamSizeEnd = 17f;
    private const float BeamRiseSpeed = 60f;
    private const float BeamLifetimeMin = 1.8f;
    private const float BeamLifetimeMax = 2.4f;
    private const float BeamEmissionRate = 9f;
    private const float BeamSpreadRadians = 0.06f;
    private const float BeamSpawnRadius = 4f;
    private const float BeamFramesPerSecond = 9f;
    private const int BeamMaxParticles = 28;

    public static EffectDefinition BuildGreenBeam(IReadOnlyList<Texture2D> frames)
    {
        if (frames is null || frames.Count == 0)
            return null;

        var beam = new EmitterDefinition
        {
            MaxParticles = BeamMaxParticles,
            EmissionRate = BeamEmissionRate,
            Lifetime = new FloatRange(BeamLifetimeMin, BeamLifetimeMax),
            InitialSpeed = FloatRange.Constant(BeamRiseSpeed),
            EmitDirection = Vector3.Up,
            DirectionSpreadRadians = BeamSpreadRadians,
            SpawnRadius = FloatRange.Constant(BeamSpawnRadius),
            Color = new ColorOverLife(BeamColorStart, BeamColorEnd),
            Size = new SizeOverLife(BeamSizeStart, BeamSizeEnd),
            Blend = ParticleBlendMode.Additive,
            Frames = frames,
            FramesPerSecond = BeamFramesPerSecond,
            LoopFrames = true,
        };

        return new EffectDefinition
        {
            Name = GreenBeamName,
            Emitters = [beam],
            Loop = true,
        };
    }
}
