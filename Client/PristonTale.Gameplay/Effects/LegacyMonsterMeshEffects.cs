using Microsoft.Xna.Framework;
using PristonTale.Effects;
using PristonTale.Effects.Effects;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Gameplay.Effects;

public sealed class LegacyMonsterMeshEffects
{
    public const int Capacity = 32;
    public const float GuardSpeed = 1800f / 256f * 70f;
    public const float PhalanxSpeed = (128f + 2000f / 256f) / 20f * 70f;
    private readonly Entry[] _entries = new Entry[Capacity];
    private readonly LegacyEffectLibrary _library;
    private readonly EffectsEngine _effects;
    private readonly PristonTransientLights _lights;
    public int Count { get; private set; }

    public LegacyMonsterMeshEffects(LegacyEffectLibrary library, EffectsEngine effects, PristonTransientLights lights)
    {
        _library = library;
        _effects = effects;
        _lights = lights;
    }

    // Facing is the authoritative simulation yaw; positions have already been converted to scene Z.
    public bool Spawn(int soundCode, Vector3 caster, float facing, Vector3 target)
    {
        if (Count == Capacity || _effects.Instances.Count >= 128)
        {
            return false;
        }

        var forward = new Vector3(MathF.Sin(facing), 0, -MathF.Cos(facing));
        var rotation = Matrix.CreateRotationY(MathHelper.Pi - facing);
        Entry entry;
        switch (soundCode)
        {
            case 0x19F0:
                entry = new Entry(soundCode, caster, Vector3.Zero, rotation, 60f / 70f, null);
                _effects.Spawn(_library.CaveMummy, caster + forward * 50f + Vector3.Up * (10000f / 256f - 20f));
                break;

            case 0x1840:
                var side = new Vector3(MathF.Cos(facing), 0, MathF.Sin(facing));
                var start = caster + side * 12f + Vector3.Up * (15500f / 256f - 24f);
                entry = new Entry(soundCode, start, forward * PhalanxSpeed, rotation, 30f / 70f,
                    _effects.Spawn(_library.IcePiercing, start));
                _lights.Add(caster, new Color(0, 150, 255), 130, 1);
                break;

            case 0x1890:
                var origin = caster + Vector3.Up * 30f;
                var destination = target + Vector3.Up * 30f;
                var delta = destination - origin;
                float distance = delta.Length();
                var direction = distance > .001f ? delta / distance : forward;
                rotation = DirectionRotation(direction);
                entry = new Entry(soundCode, origin, direction * GuardSpeed, rotation,
                    MathF.Min(200f / 70f, MathF.Max(0, distance - 5f) / GuardSpeed),
                    _effects.Spawn(_library.JavelinTrail, origin, rotation));
                entry.Hit = distance <= GuardSpeed * (200f / 70f) + 5f;
                break;

            default:
                return false;
        }

        _entries[Count++] = entry;
        return true;
    }

    public static Matrix DirectionRotation(Vector3 direction)
    {
        // Scripts use legacy +Z, converted to scene -Z. Preserve an orthonormal basis near vertical shots.
        var up = MathF.Abs(Vector3.Dot(direction, Vector3.Up)) > .99f ? Vector3.Right : Vector3.Up;
        return Matrix.CreateWorld(Vector3.Zero, direction, up);
    }

    public void Update(float seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        int index = 0;
        while (index < Count)
        {
            ref var entry = ref _entries[index];
            entry.Age += seconds;
            var position = entry.Origin + entry.Velocity * MathF.Min(entry.Age, entry.Duration);
            if (entry.Trail is not null)
            {
                entry.Trail.Position = position;
                if (entry.Code == 0x1840 && entry.Age >= 19f / 70f)
                {
                    entry.Trail.Stop();
                }
            }

            if (entry.Age < entry.Duration)
            {
                index++;
                continue;
            }

            entry.Trail?.Stop();
            if (entry.Code == 0x1890 && entry.Hit && _effects.Instances.Count < 128)
            {
                _effects.Spawn(_library.JavelinHit, position, entry.Rotation);
            }

            _entries[index] = _entries[--Count];
            _entries[Count] = default;
        }
    }

    public void Draw(Matrix view, Matrix projection)
    {
        for (int index = 0; index < Count; index++)
        {
            ref var entry = ref _entries[index];
            var position = entry.Origin + entry.Velocity * entry.Age;
            var world = entry.Rotation * Matrix.CreateTranslation(position);
            float fade = MathHelper.Clamp((entry.Duration - entry.Age) / (entry.Duration * .255f), 0, 1);
            switch (entry.Code)
            {
                case 0x19F0:
                    DrawHest(_library.HestMesh, entry, 1, 10000f / 256f, view, projection);
                    DrawHest(_library.HestMesh2, entry, 2, 12000f / 256f, view, projection);
                    break;

                case 0x1840:
                    _library.PiercingMesh.Draw(0, world, view, projection, fade);
                    break;

                case 0x1890:
                    var spin = Matrix.CreateRotationZ(entry.Age * 70f * 100f * MathHelper.TwoPi / 4096f);
                    _library.ArrowMesh.Draw(0, spin * world, view, projection, fade);
                    break;
            }
        }
    }

    private static void DrawHest(PristonMeshEffect mesh, in Entry entry, int delay, float height, Matrix view, Matrix projection)
    {
        float ticks = entry.Age * 70f;
        float duration = 30f * delay;
        if (ticks >= duration)
        {
            return;
        }

        float opacity = MathHelper.Clamp(MathF.Min(ticks, duration - ticks) / (duration * .255f), 0, 1);
        var world = entry.Rotation * Matrix.CreateTranslation(entry.Origin + Vector3.Up * height);
        mesh.Draw(Math.Min(29, 1 + (int)(ticks / delay)), world, view, projection, opacity);
    }

    public void Clear()
    {
        for (int index = 0; index < Count; index++)
        {
            _entries[index].Trail?.Stop();
            _entries[index] = default;
        }

        Count = 0;
    }

    private struct Entry(int code, Vector3 origin, Vector3 velocity, Matrix rotation, float duration, EffectInstance trail)
    {
        public int Code = code;
        public Vector3 Origin = origin;
        public Vector3 Velocity = velocity;
        public Matrix Rotation = rotation;
        public float Duration = duration;
        public EffectInstance Trail = trail;
        public float Age;
        public bool Hit;
    }
}
