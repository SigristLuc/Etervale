using Microsoft.Xna.Framework;
using PristonTale.Effects.Particles;

namespace PristonTale.Effects.Legacy;

public enum LegacyParticleProperty
{
    Size,
    SizeExt,
    Color,
    Velocity,
    PartAngle
}

public readonly record struct LegacyParticleRange(Vector4 Min, Vector4 Max)
{
    public Vector4 Sample(uint seed)
    {
        return new Vector4(
            SampleComponent(Min.X, Max.X, seed),
            SampleComponent(Min.Y, Max.Y, seed + 1),
            SampleComponent(Min.Z, Max.Z, seed + 2),
            SampleComponent(Min.W, Max.W, seed + 3));
    }

    private static float SampleComponent(float min, float max, uint seed)
    {
        // Stable independent channels; evaluating a timeline never consumes a mutable RNG stream.
        seed = unchecked((seed ^ (seed >> 16)) * 0x7feb352d);
        seed = unchecked((seed ^ (seed >> 15)) * 0x846ca68b);
        seed ^= seed >> 16;
        return min + (max - min) * ((seed >> 8) / 16777216f);
    }
}

public readonly record struct LegacyParticleEvent(
    LegacyParticleProperty Property, float Time, bool Fade, LegacyParticleRange Value);

public sealed class LegacyParticleSequence
{
    public required string Texture { get; init; }
    public required int Count { get; init; }
    public int Loops { get; init; } = 1;
    public int TotalCount => Count * Loops;
    public float Delay { get; init; }
    public Vector3 Gravity { get; init; }
    public bool GroundPlane { get; init; }
    public PristonTale.Effects.Rendering.ParticleBlendMode Blend { get; init; }
    public required float EmitRate { get; init; }
    public required LegacyParticleRange Lifetime { get; init; }
    public required LegacyParticleRange Radius { get; init; }
    public required IReadOnlyList<LegacyParticleEvent> Events { get; init; }

    public void Initialize(ref Particle particle, Vector3 origin, Random random)
    {
        Initialize(ref particle, origin, (uint)random.Next());
    }

    public void Initialize(ref Particle particle, Vector3 origin, uint seed, Matrix? orientation = null)
    {
        particle.ScriptSeed = seed;
        var offset = Radius.Sample(particle.ScriptSeed);
        particle.Origin = origin + Vector3.TransformNormal(new Vector3(offset.X, offset.Y, -offset.Z), orientation ?? Matrix.Identity);
        particle.Lifetime = Lifetime.Sample(particle.ScriptSeed + 4).X;
    }

    public void Evaluate(ref Particle particle, Matrix? orientation = null)
    {
        float age = particle.Age;
        uint seed = particle.ScriptSeed;
        var size = Sample(LegacyParticleProperty.Size, age, seed, Vector4.One).X;
        var extension = Sample(LegacyParticleProperty.SizeExt, age, seed, Vector4.Zero).X;
        particle.Size = MathF.Max(0, size) * .5f;
        particle.Height = MathF.Max(0, extension == 0 ? size : extension) * .5f;
        var color = Sample(LegacyParticleProperty.Color, age, seed, new Vector4(255));
        particle.Color = new Color(Vector4.Clamp(color / 255f, Vector4.Zero, Vector4.One));
        float angle = Sample(LegacyParticleProperty.PartAngle, age, seed, Vector4.Zero).Z;
        particle.Rotation = ((int)angle & 4095) * (MathHelper.TwoPi / 4096f);
        var displacement = IntegrateVelocity(age, seed);
        particle.Position = particle.Origin + Vector3.TransformNormal(new Vector3(displacement.X, displacement.Y, -displacement.Z), orientation ?? Matrix.Identity);
    }

    private Vector4 Sample(LegacyParticleProperty property, float age, uint seed, Vector4 initial)
    {
        float previousTime = 0;
        var previous = initial;
        for (int index = 0; index < Events.Count; index++)
        {
            var entry = Events[index];
            if (entry.Property != property)
            {
                continue;
            }

            var value = entry.Value.Sample(unchecked(seed + 8u + (uint)index * 4u));
            if (entry.Time > age)
            {
                if (entry.Fade && entry.Time > previousTime)
                {
                    return Vector4.Lerp(previous, value, (age - previousTime) / (entry.Time - previousTime));
                }

                return previous;
            }

            previousTime = entry.Time;
            previous = value;
        }

        return previous;
    }

    private Vector4 IntegrateVelocity(float age, uint seed)
    {
        var distance = Vector4.Zero;
        var velocity = Vector4.Zero;
        float previousTime = 0;
        for (int index = 0; index < Events.Count; index++)
        {
            var entry = Events[index];
            if (entry.Property != LegacyParticleProperty.Velocity)
            {
                continue;
            }

            float end = MathF.Min(age, entry.Time);
            float duration = end - previousTime;
            distance += velocity * duration + new Vector4(Gravity, 0) * (.5f * duration * duration);
            if (entry.Time >= age)
            {
                return distance;
            }

            velocity = entry.Value.Sample(unchecked(seed + 8u + (uint)index * 4u));
            previousTime = entry.Time;
        }

        float remaining = age - previousTime;
        return distance + velocity * remaining + new Vector4(Gravity, 0) * (.5f * remaining * remaining);
    }
}
