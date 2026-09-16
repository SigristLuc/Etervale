using Microsoft.Xna.Framework;

namespace PristonTale.Effects.Particles;

public static class PotionParticleMotion
{
    public const float TickRate = 70f;

    public static void Initialize(ref Particle particle, Random random, float fadeSeconds)
    {
        var angle = random.Next(4096) * MathHelper.TwoPi / 4096f;
        var ticks = random.Next(55, 75);
        particle.Velocity = new Vector3((int)(MathF.Cos(angle) * 70f), 70f, (int)(MathF.Sin(angle) * 70f));
        particle.AccelerationY = -random.Next(3, 6);
        particle.MotionDuration = ticks / TickRate;
        particle.OrbitStep = (4096 / 3 + 500) / ticks;
        particle.Lifetime = particle.MotionDuration + fadeSeconds;
    }

    public static Vector3 Position(in Particle particle)
    {
        var ticks = MathF.Min(particle.Age, particle.MotionDuration) * TickRate;
        var x = particle.Velocity.X * ticks / 256f;
        var z = particle.Velocity.Z * ticks / 256f;
        var y = (particle.Velocity.Y * ticks + particle.AccelerationY * ticks * (ticks + 1f) * 0.5f) / 256f;
        var angle = ticks * particle.OrbitStep * MathHelper.TwoPi / 4096f;
        var (sin, cos) = MathF.SinCos(angle);
        // Legacy world Z is inverted by the scene coordinate mapping.
        return particle.Origin + new Vector3(x * cos - z * sin, y, -(x * sin + z * cos));
    }
}
