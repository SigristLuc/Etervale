using Microsoft.Xna.Framework;
using PristonTale.Effects.Legacy;
using PristonTale.Effects.Rendering;

namespace PristonTale.Effects.Vfx;

// Converts an original particle script into the native authoring format, as a starting point for remastered
// effects. A script sequence becomes one emitter: count and loops give the burst and rate, the emit radius a sphere,
// the timeline events (absolute seconds) size and colour keys over the mean lifetime, the first velocity event the
// launch velocity. Exact original playback stays available through a legacy layer (VfxLegacyDefinition).
public static class LegacyVfxImporter
{
    public static VfxDefinition Convert(string name, IReadOnlyList<LegacyParticleSequence> sequences, float intensity = 1f)
    {
        ArgumentNullException.ThrowIfNull(sequences);
        var emitters = new List<VfxEmitterDefinition>(sequences.Count);
        var duration = 0.1f;
        foreach (var sequence in sequences)
        {
            var lifetime = new[] { MathF.Max(0.02f, sequence.Lifetime.Min.X), MathF.Max(0.02f, MathF.Max(sequence.Lifetime.Min.X, sequence.Lifetime.Max.X)) };
            var meanLife = (lifetime[0] + lifetime[1]) * 0.5f;
            var emitTime = sequence.EmitRate > 0f && sequence.Loops > 1 ? sequence.Loops / MathF.Max(sequence.EmitRate, 0.001f) : 0f;
            duration = MathF.Max(duration, sequence.Delay + emitTime + lifetime[1]);
            var radius = MathF.Max(MathF.Abs(sequence.Radius.Max.X), MathF.Max(MathF.Abs(sequence.Radius.Max.Y), MathF.Abs(sequence.Radius.Max.Z)));
            var velocity = FirstValue(sequence, LegacyParticleProperty.Velocity, Vector4.Zero);
            var velocityMax = FirstMax(sequence, LegacyParticleProperty.Velocity, Vector4.Zero);
            var speedLow = new Vector3(velocity.X, velocity.Y, -velocity.Z).Length();
            var speedHigh = new Vector3(velocityMax.X, velocityMax.Y, -velocityMax.Z).Length();
            var mean = new Vector3(velocity.X + velocityMax.X, velocity.Y + velocityMax.Y, -(velocity.Z + velocityMax.Z)) * 0.5f;
            var spread = speedHigh > 0f && (velocity - velocityMax).Length() > speedHigh * 0.5f ? 180f : 25f;
            emitters.Add(new VfxEmitterDefinition
            {
                Texture = sequence.Texture,
                Blend = sequence.Blend == ParticleBlendMode.Additive ? VfxBlend.Additive : VfxBlend.Alpha,
                Intensity = intensity,
                Softness = sequence.GroundPlane ? 0f : 12f,
                Delay = sequence.Delay,
                Burst = sequence.Count,
                Rate = sequence.Loops > 1 ? sequence.EmitRate * sequence.Count : 0f,
                EmitTime = emitTime,
                MaxParticles = Math.Clamp(sequence.TotalCount, 1, 4096),
                Lifetime = lifetime,
                Speed = [MathF.Min(speedLow, speedHigh), MathF.Max(speedLow, speedHigh)],
                Direction = mean.LengthSquared() > 1e-4f ? [mean.X, mean.Y, mean.Z] : [0f, 1f, 0f],
                Spread = spread,
                Shape = radius > 0f ? VfxShape.Sphere : VfxShape.Point,
                Radius = [0f, radius],
                Gravity = [sequence.Gravity.X, sequence.Gravity.Y, -sequence.Gravity.Z],
                Size = Keys(sequence, LegacyParticleProperty.Size, meanLife, value => [value.X * 0.5f], [0.5f]),
                Color = Keys(sequence, LegacyParticleProperty.Color, meanLife,
                    value => [value.X / 255f, value.Y / 255f, value.Z / 255f, value.W / 255f], [1f, 1f, 1f, 1f]),
                Align = sequence.GroundPlane ? VfxAlign.Ground : VfxAlign.Camera
            });
        }

        return new VfxDefinition { Name = name, Duration = MathF.Min(60f, duration), Emitters = emitters.ToArray() };
    }

    private static VfxKey[] Keys(LegacyParticleSequence sequence, LegacyParticleProperty property, float meanLife, Func<Vector4, float[]> map, float[] initial)
    {
        var keys = new List<VfxKey>();
        foreach (var entry in sequence.Events)
        {
            if (entry.Property != property)
            {
                continue;
            }

            var mid = (entry.Value.Min + entry.Value.Max) * 0.5f;
            var t = Math.Clamp(entry.Time / meanLife, 0f, 1f);
            if (keys.Count > 0 && t < keys[^1].T)
            {
                t = keys[^1].T;
            }

            if (!entry.Fade && keys.Count > 0 && t > keys[^1].T)
            {
                // A step: hold the previous value until the event.
                keys.Add(new VfxKey(t, keys[^1].V));
            }

            keys.Add(new VfxKey(t, map(mid)));
        }

        return keys.Count == 0 ? [new VfxKey(0f, initial)] : keys.ToArray();
    }

    private static Vector4 FirstValue(LegacyParticleSequence sequence, LegacyParticleProperty property, Vector4 fallback)
    {
        foreach (var entry in sequence.Events)
        {
            if (entry.Property == property)
            {
                return entry.Value.Min;
            }
        }

        return fallback;
    }

    private static Vector4 FirstMax(LegacyParticleSequence sequence, LegacyParticleProperty property, Vector4 fallback)
    {
        foreach (var entry in sequence.Events)
        {
            if (entry.Property == property)
            {
                return entry.Value.Max;
            }
        }

        return fallback;
    }
}
