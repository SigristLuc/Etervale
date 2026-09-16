using Microsoft.Xna.Framework;

namespace PristonTale.Effects.Particles;

public struct Particle
{
    public Vector3 Position;
    public Vector3 Origin;
    public float AccelerationY;
    public float MotionDuration;
    public int OrbitStep;

    public Vector3 Velocity;

    public float Age;

    public float Lifetime;

    public float Size;
    public float Height;
    public uint ScriptSeed;

    public float Rotation;

    public float RotationSpeed;

    public Color Color;

    public int Frame;

    public float Seed01;

    public readonly float Life01
    {
        get
        {
            if (Lifetime <= 0f)
                return 1f;
            var t = Age / Lifetime;
            return t < 0f ? 0f : (t > 1f ? 1f : t);
        }
    }
}
