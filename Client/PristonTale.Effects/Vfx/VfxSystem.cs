using System.Buffers;
using Microsoft.Xna.Framework;
using PristonTale.Effects.Effects;

namespace PristonTale.Effects.Vfx;

// Positions the effects attach to. Entity ids are the server serials; positions are client world space.
public interface IVfxAnchors
{
    bool TryGetPosition(uint entityId, out Vector3 position);

    bool TryGetBone(uint entityId, string bone, out Vector3 position);
}

// Side effects of a layer that live outside the particle system: scene lights, sounds and legacy scripts.
public interface IVfxServices
{
    void AddLight(Vector3 position, Color color, int power, int decay);

    void PlaySound(string path, Vector3 position, float volume);

    EffectInstance? SpawnLegacy(string script, Vector3 position, Matrix orientation);
}

public struct VfxParticle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Age;
    public float Lifetime;
    public float Rotation;
    public float Spin;
}

public readonly record struct VfxSpawn(uint CasterId, uint TargetId, Vector3 Point, float Facing);

// Runs every native effect instance. Instances, particle arrays and trail buffers are pooled, so a frame allocates
// nothing once the pools have warmed up; the budget caps live instances so a crowd of casters cannot run away.
public sealed class VfxSystem
{
    public const int MaxInstances = 256;
    internal const int TrailPoints = 32;

    private readonly List<VfxInstance> _live = new(64);
    private readonly Stack<VfxInstance> _free = new();
    private readonly IVfxAnchors _anchors;
    private readonly IVfxServices? _services;
    private uint _seed = 0x9E3779B9;

    public VfxSystem(IVfxAnchors anchors, IVfxServices? services = null)
    {
        _anchors = anchors;
        _services = services;
    }

    public IReadOnlyList<VfxInstance> Instances => _live;

    public int ParticleCount
    {
        get
        {
            var count = 0;
            foreach (var instance in _live)
            {
                foreach (var emitter in instance.EmitterStates)
                {
                    count += emitter.Count;
                }
            }

            return count;
        }
    }

    public VfxInstance? Spawn(CompiledVfx effect, in VfxSpawn spawn)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (_live.Count >= MaxInstances)
        {
            return null;
        }

        var instance = _free.Count > 0 ? _free.Pop() : new VfxInstance();
        _seed = unchecked(_seed * 1664525u + 1013904223u);
        instance.Begin(effect, spawn, _seed);
        _live.Add(instance);
        return instance;
    }

    public void Update(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f)
        {
            return;
        }

        seconds = MathF.Min(seconds, 0.1f);
        for (var index = _live.Count - 1; index >= 0; index--)
        {
            var instance = _live[index];
            instance.Update(seconds, _anchors, _services);
            if (!instance.Alive)
            {
                instance.Release();
                _live.RemoveAt(index);
                _free.Push(instance);
            }
        }
    }

    public void Clear()
    {
        foreach (var instance in _live)
        {
            instance.Release();
            _free.Push(instance);
        }

        _live.Clear();
    }
}

public sealed class VfxInstance
{
    internal EmitterState[] EmitterStates = [];
    internal TrailState[] TrailStates = [];
    private EffectInstance?[] _legacy = [];
    private bool[] _lightsFired = [];
    private bool[] _soundsFired = [];
    private bool[] _legacyFired = [];
    private uint _random;
    private bool _stopped;
    private bool _travelling;
    private Vector3 _travelFrom;
    private Vector3 _travelTo;
    private float _travelSeconds;
    private float _travelAge;
    private float _arc;

    public CompiledVfx Effect { get; private set; } = null!;
    public VfxSpawn Spawn { get; private set; }
    public float Age { get; private set; }
    public bool Alive { get; private set; }
    public Vector3 CasterPosition { get; private set; }
    public Vector3 TargetPosition { get; private set; }
    public Matrix Orientation { get; private set; }

    // Ends emission now; live particles finish their lives.
    public void Stop() => _stopped = true;

    // Moves the spawn point from one position to another over the given time (projectiles); a slight arc lifts the
    // middle of the flight. The effect stops emitting when it arrives.
    public void Travel(Vector3 from, Vector3 to, float seconds, float arc = 0f)
    {
        _travelFrom = from;
        _travelTo = to;
        _travelSeconds = MathF.Max(0.01f, seconds);
        _travelAge = 0f;
        _arc = arc;
        _travelling = true;
        Spawn = Spawn with { Point = from };
    }

    public bool Travelling => _travelling;

    internal void Begin(CompiledVfx effect, in VfxSpawn spawn, uint seed)
    {
        Effect = effect;
        Spawn = spawn;
        Age = 0f;
        Alive = true;
        _stopped = false;
        _travelling = false;
        _random = seed | 1u;
        CasterPosition = spawn.Point;
        TargetPosition = spawn.Point;
        Orientation = Matrix.CreateRotationY(spawn.Facing);
        EmitterStates = Resize(EmitterStates, effect.Emitters.Length);
        for (var index = 0; index < effect.Emitters.Length; index++)
        {
            EmitterStates[index] ??= new EmitterState();
            EmitterStates[index].Begin(effect.Emitters[index]);
        }

        TrailStates = Resize(TrailStates, effect.Trails.Length);
        for (var index = 0; index < effect.Trails.Length; index++)
        {
            TrailStates[index] ??= new TrailState();
            TrailStates[index].Begin(effect.Trails[index]);
        }

        var definition = effect.Definition;
        _lightsFired = ResizeCleared(_lightsFired, definition.Lights.Length);
        _soundsFired = ResizeCleared(_soundsFired, definition.Sounds.Length);
        _legacyFired = ResizeCleared(_legacyFired, definition.Legacy.Length);
        if (_legacy.Length != definition.Legacy.Length)
        {
            _legacy = new EffectInstance?[definition.Legacy.Length];
        }
        else
        {
            Array.Clear(_legacy);
        }
    }

    internal void Release()
    {
        foreach (var emitter in EmitterStates)
        {
            emitter?.Release();
        }

        for (var index = 0; index < _legacy.Length; index++)
        {
            _legacy[index]?.Stop();
            _legacy[index] = null;
        }
    }

    internal Vector3 AnchorPosition(VfxAnchor anchor, string bone, Vector3 offset, IVfxAnchors anchors)
    {
        var entity = anchor switch
        {
            VfxAnchor.Caster => Spawn.CasterId,
            VfxAnchor.Target => Spawn.TargetId,
            _ => 0u
        };
        var basis = anchor switch
        {
            VfxAnchor.Caster => CasterPosition,
            VfxAnchor.Target => TargetPosition,
            _ => Spawn.Point
        };
        if (entity != 0 && bone.Length > 0 && anchors.TryGetBone(entity, bone, out var bonePosition))
        {
            basis = bonePosition;
        }

        return basis + Vector3.TransformNormal(offset, Orientation);
    }

    internal void Update(float dt, IVfxAnchors anchors, IVfxServices? services)
    {
        Age += dt;
        if (_travelling)
        {
            _travelAge += dt;
            var t = MathF.Min(1f, _travelAge / _travelSeconds);
            var point = Vector3.Lerp(_travelFrom, _travelTo, t);
            point.Y += _arc * 4f * t * (1f - t);
            Spawn = Spawn with { Point = point };
            if (t >= 1f)
            {
                _travelling = false;
                _stopped = true;
            }
        }

        if (Spawn.CasterId != 0 && anchors.TryGetPosition(Spawn.CasterId, out var caster))
        {
            CasterPosition = caster;
        }

        if (Spawn.TargetId != 0 && anchors.TryGetPosition(Spawn.TargetId, out var target))
        {
            TargetPosition = target;
        }

        var definition = Effect.Definition;
        var emitting = !_stopped && Age <= definition.Duration;
        var any = emitting;
        for (var index = 0; index < EmitterStates.Length; index++)
        {
            any |= EmitterStates[index].Update(this, dt, emitting, anchors, ref _random);
        }

        for (var index = 0; index < TrailStates.Length; index++)
        {
            any |= TrailStates[index].Update(this, dt, emitting, anchors);
        }

        FireOneShots(definition, anchors, services);
        for (var index = 0; index < _legacy.Length; index++)
        {
            var legacy = _legacy[index];
            if (legacy is null)
            {
                continue;
            }

            var layer = definition.Legacy[index];
            if (layer.Follow)
            {
                legacy.Position = AnchorPosition(layer.Anchor, layer.Bone, VfxDefinition.Vec3(layer.Offset), anchors);
            }

            if (_stopped)
            {
                legacy.Stop();
            }

            if (legacy.IsAlive)
            {
                any = true;
            }
            else
            {
                _legacy[index] = null;
            }
        }

        Alive = any;
    }

    private void FireOneShots(VfxDefinition definition, IVfxAnchors anchors, IVfxServices? services)
    {
        if (services is null || _stopped)
        {
            return;
        }

        for (var index = 0; index < definition.Lights.Length; index++)
        {
            var light = definition.Lights[index];
            if (!_lightsFired[index] && Age >= light.Delay)
            {
                _lightsFired[index] = true;
                var position = AnchorPosition(light.Anchor, "", VfxDefinition.Vec3(light.Offset), anchors);
                services.AddLight(position, new Color(light.Color[0], light.Color[1], light.Color[2]), light.Power, light.Decay);
            }
        }

        for (var index = 0; index < definition.Sounds.Length; index++)
        {
            var sound = definition.Sounds[index];
            if (!_soundsFired[index] && Age >= sound.Delay)
            {
                _soundsFired[index] = true;
                services.PlaySound(sound.Path, AnchorPosition(VfxAnchor.Caster, "", Vector3.Zero, anchors), sound.Volume);
            }
        }

        for (var index = 0; index < definition.Legacy.Length; index++)
        {
            var layer = definition.Legacy[index];
            if (!_legacyFired[index] && Age >= layer.Delay)
            {
                _legacyFired[index] = true;
                var position = AnchorPosition(layer.Anchor, layer.Bone, VfxDefinition.Vec3(layer.Offset), anchors);
                _legacy[index] = services.SpawnLegacy(layer.Script, position, Orientation);
            }
        }
    }

    internal static float NextUnit(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state >> 8) / 16777216f;
    }

    private static T[] Resize<T>(T[] array, int length) where T : class
    {
        if (array.Length == length)
        {
            return array;
        }

        var resized = new T[length];
        Array.Copy(array, resized, Math.Min(array.Length, length));
        return resized;
    }

    private static bool[] ResizeCleared(bool[] array, int length)
    {
        if (array.Length != length)
        {
            return new bool[length];
        }

        Array.Clear(array);
        return array;
    }
}

internal sealed class EmitterState
{
    public VfxParticle[] Particles = [];
    public int Count;
    public CompiledEmitter Emitter = null!;
    public Vector3 LastOrigin;
    private float _accumulator;
    private bool _burst;

    public void Begin(CompiledEmitter emitter)
    {
        Emitter = emitter;
        if (Particles.Length < emitter.Source.MaxParticles)
        {
            Release();
            Particles = ArrayPool<VfxParticle>.Shared.Rent(emitter.Source.MaxParticles);
        }

        Count = 0;
        _accumulator = 0f;
        _burst = false;
    }

    public void Release()
    {
        if (Particles.Length > 0)
        {
            ArrayPool<VfxParticle>.Shared.Return(Particles);
            Particles = [];
        }

        Count = 0;
    }

    public bool Update(VfxInstance instance, float dt, bool emitting, IVfxAnchors anchors, ref uint random)
    {
        var source = Emitter.Source;
        var localAge = instance.Age - source.Delay;
        var origin = instance.AnchorPosition(source.Anchor, source.Bone, Emitter.Offset, anchors);
        var shift = origin - LastOrigin;
        LastOrigin = origin;
        var active = emitting && localAge >= 0f && (source.EmitTime <= 0f || localAge <= source.EmitTime);
        if (active && !_burst)
        {
            _burst = true;
            shift = Vector3.Zero;
            for (var index = 0; index < source.Burst; index++)
            {
                Emit(instance, origin, ref random);
            }
        }

        if (active && source.Rate > 0f)
        {
            _accumulator += source.Rate * dt;
            while (_accumulator >= 1f)
            {
                _accumulator -= 1f;
                Emit(instance, origin, ref random);
            }
        }

        var drag = source.Drag > 0f ? MathF.Max(0f, 1f - source.Drag * dt) : 1f;
        var index2 = 0;
        while (index2 < Count)
        {
            ref var particle = ref Particles[index2];
            particle.Age += dt;
            if (particle.Age >= particle.Lifetime)
            {
                Particles[index2] = Particles[--Count];
                continue;
            }

            particle.Velocity = (particle.Velocity + Emitter.Gravity * dt) * drag;
            particle.Position += particle.Velocity * dt;
            if (source.Local)
            {
                particle.Position += shift;
            }

            particle.Rotation += particle.Spin * dt;
            index2++;
        }

        return Count > 0 || emitting && localAge < 0f || active;
    }

    private void Emit(VfxInstance instance, Vector3 origin, ref uint random)
    {
        if (Count >= Emitter.Source.MaxParticles)
        {
            return;
        }

        var source = Emitter.Source;
        ref var particle = ref Particles[Count++];
        var offset = ShapeOffset(source, ref random);
        particle.Position = origin + Vector3.TransformNormal(offset, instance.Orientation);
        var direction = Vector3.TransformNormal(ConeDirection(Emitter.Direction, Emitter.SpreadCos, source.Shape, offset, ref random), instance.Orientation);
        particle.Velocity = direction * Lerp(source.Speed, VfxInstance.NextUnit(ref random));
        particle.Age = 0f;
        particle.Lifetime = Lerp(source.Lifetime, VfxInstance.NextUnit(ref random));
        particle.Rotation = MathHelper.ToRadians(Lerp(source.Rotation, VfxInstance.NextUnit(ref random)));
        particle.Spin = MathHelper.ToRadians(Lerp(source.Spin, VfxInstance.NextUnit(ref random)));
    }

    private static Vector3 ShapeOffset(VfxEmitterDefinition source, ref uint random)
    {
        var radius = Lerp(source.Radius, VfxInstance.NextUnit(ref random));
        var angle = VfxInstance.NextUnit(ref random) * MathHelper.TwoPi;
        switch (source.Shape)
        {
            case VfxShape.Ring:
                return new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
            case VfxShape.Disc:
                var r = radius * MathF.Sqrt(VfxInstance.NextUnit(ref random));
                return new Vector3(MathF.Cos(angle) * r, 0f, MathF.Sin(angle) * r);
            case VfxShape.Sphere:
                var z = VfxInstance.NextUnit(ref random) * 2f - 1f;
                var planar = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
                return new Vector3(MathF.Cos(angle) * planar, z, MathF.Sin(angle) * planar) * radius;
            default:
                return Vector3.Zero;
        }
    }

    // A random direction within the cone; ring and disc shapes with no spread push outwards from the centre.
    private static Vector3 ConeDirection(Vector3 axis, float spreadCos, VfxShape shape, Vector3 offset, ref uint random)
    {
        if (spreadCos >= 0.9999f)
        {
            return shape is VfxShape.Ring or VfxShape.Disc or VfxShape.Sphere && offset.LengthSquared() > 1e-4f ? Vector3.Normalize(offset) : axis;
        }

        var cos = 1f - VfxInstance.NextUnit(ref random) * (1f - spreadCos);
        var sin = MathF.Sqrt(MathF.Max(0f, 1f - cos * cos));
        var phi = VfxInstance.NextUnit(ref random) * MathHelper.TwoPi;
        var tangent = MathF.Abs(axis.Y) < 0.99f ? Vector3.Normalize(Vector3.Cross(axis, Vector3.Up)) : Vector3.Right;
        var bitangent = Vector3.Cross(axis, tangent);
        return axis * cos + (tangent * MathF.Cos(phi) + bitangent * MathF.Sin(phi)) * sin;
    }

    private static float Lerp(float[] range, float t) => range[0] + (range[1] - range[0]) * t;
}

internal sealed class TrailState
{
    public readonly Vector3[] Points = new Vector3[VfxSystem.TrailPoints];
    public readonly float[] Ages = new float[VfxSystem.TrailPoints];
    public int Head;
    public int Count;
    public CompiledTrail Trail = null!;
    private float _sinceSample;

    public void Begin(CompiledTrail trail)
    {
        Trail = trail;
        Head = 0;
        Count = 0;
        _sinceSample = float.MaxValue;
    }

    public bool Update(VfxInstance instance, float dt, bool emitting, IVfxAnchors anchors)
    {
        var source = Trail.Source;
        for (var index = 0; index < Count; index++)
        {
            Ages[(Head - index + Points.Length) % Points.Length] += dt;
        }

        while (Count > 0 && Ages[(Head - Count + 1 + Points.Length) % Points.Length] > source.Lifetime)
        {
            Count--;
        }

        var localAge = instance.Age - source.Delay;
        var active = emitting && localAge >= 0f && (source.EmitTime <= 0f || localAge <= source.EmitTime);
        _sinceSample += dt;
        if (active && _sinceSample >= source.Lifetime / Points.Length)
        {
            _sinceSample = 0f;
            Head = (Head + 1) % Points.Length;
            Points[Head] = instance.AnchorPosition(source.Anchor, source.Bone, Trail.Offset, anchors);
            Ages[Head] = 0f;
            Count = Math.Min(Count + 1, Points.Length);
        }

        return Count > 0 || active || emitting && localAge < 0f;
    }
}
