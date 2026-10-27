using Microsoft.Xna.Framework;
using PristonTale.Contracts.Units;

namespace PristonTale.Effects.Particles;

public static class PotionParticleMotion
{

    public static void Initialize(ref Particle particle, Random random, float fadeSeconds)
    {
        var angle = random.Next(GameUnits.AngleTurn) * GameUnits.RadiansPerAngle;
        var ticks = random.Next(55, 75);
        particle.Velocity = new Vector3((int)(MathF.Cos(angle) * 70f), 70f, (int)(MathF.Sin(angle) * 70f));
        particle.AccelerationY = -random.Next(3, 6);
        particle.MotionDuration = ticks * GameUnits.TickSeconds;
        particle.OrbitStep = (GameUnits.AngleTurn / 3 + 500) / ticks;
        particle.Lifetime = particle.MotionDuration + fadeSeconds;
    }

    public static Vector3 Position(in Particle particle)
    {
        var ticks = MathF.Min(particle.Age, particle.MotionDuration) * GameUnits.TicksPerSecond;
        var x = particle.Velocity.X * ticks / GameUnits.FixedScale;
        var z = particle.Velocity.Z * ticks / GameUnits.FixedScale;
        var y = (particle.Velocity.Y * ticks + particle.AccelerationY * ticks * (ticks + 1f) * 0.5f) / GameUnits.FixedScale;
        var angle = ticks * particle.OrbitStep * GameUnits.RadiansPerAngle;
        var (sin, cos) = MathF.SinCos(angle);
        // Original world Z is inverted by the scene coordinate mapping.
        return particle.Origin + new Vector3(x * cos - z * sin, y, -(x * sin + z * cos));
    }
}
