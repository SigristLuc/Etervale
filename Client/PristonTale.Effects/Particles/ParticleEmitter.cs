using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Rendering;

namespace PristonTale.Effects.Particles;

public sealed class ParticleEmitter
{
    private readonly EmitterDefinition _definition;
    private readonly Random _random;
    private readonly Particle[] _particles;
    private int _aliveCount;
    private double _scriptAge;
    private readonly uint _scriptSeed;
    private Vector3 _previousOrigin;
    private readonly Matrix _orientation;
    private int _scriptBirths;

    // Fractional spawn accumulator so a continuous EmissionRate spawns correctly across variable dt
    // without rounding to zero each frame (e.g. 3 particles/sec at 60 fps).
    private float _spawnAccumulator;
    private bool _emitting = true;

    public ParticleEmitter(EmitterDefinition definition, Vector3 origin, int seed, Matrix? orientation = null)
    {
        _definition = definition;
        _orientation = orientation ?? Matrix.Identity;
        _scriptSeed = (uint)seed;
        _previousOrigin = origin;
        _random = new Random(seed);
        var capacity = definition.MaxParticles < 1 ? 1 : definition.MaxParticles;
        _particles = new Particle[capacity];

        for (var i = 0; i < definition.BurstCount; i++)
        {
            Spawn(origin);
        }
    }

    public EmitterDefinition Definition => _definition;

    public ParticleBlendMode Blend => _definition.Blend;

    public int AliveCount => _aliveCount;

    public bool IsAlive => _emitting || _aliveCount > 0;

    public void Stop() => _emitting = false;

    public void Update(float dt, Vector3 origin)
    {
        if (dt < 0f)
        {
            dt = 0f;
        }

        if (_definition.LegacySequence is { } script)
        {
            UpdateScript(script, dt, origin);
            return;
        }

        if (_emitting && _definition.EmissionRate > 0f)
        {
            _spawnAccumulator += _definition.EmissionRate * dt;
            while (_spawnAccumulator >= 1f)
            {
                _spawnAccumulator -= 1f;
                Spawn(origin);
            }
        }

        // Frame-rate-independent damping: surviving fraction over dt seconds.
        var dampScale = _definition.Damping > 0f
            ? (float)Math.Pow(1.0 - Math.Min(_definition.Damping, 0.999999), dt)
            : 1f;

        var i = 0;
        while (i < _aliveCount)
        {
            ref var p = ref _particles[i];
            p.Age += dt;
            if (p.Age >= p.Lifetime)
            {
                _aliveCount--;
                _particles[i] = _particles[_aliveCount];
                continue;
            }

            if (_definition.PotionMotion)
            {
                p.Position = PotionParticleMotion.Position(p);
            }
            else
            {
                p.Velocity += _definition.Gravity * dt;
                p.Velocity *= dampScale;
                p.Position += p.Velocity * dt;
            }
            p.Rotation += p.RotationSpeed * dt;

            var life01 = p.Life01;
            p.Color = _definition.Color.At(life01);
            p.Size = _definition.Size.At(life01);
            p.Frame = ResolveFrame(p, life01);
            ApplyAnimation(ref p);
            i++;
        }
    }

    private void UpdateScript(PristonTale.Effects.Legacy.LegacyParticleSequence script, float dt, Vector3 origin)
    {
        double previousAge = _scriptAge;
        _scriptAge += dt;
        int index = 0;
        while (index < _aliveCount)
        {
            ref var particle = ref _particles[index];
            particle.Age += dt;
            if (particle.Age >= particle.Lifetime)
            {
                _particles[index] = _particles[--_aliveCount];
                continue;
            }

            script.Evaluate(ref particle, _orientation);
            index++;
        }

        // Birth timestamps make finite scripts independent of render FPS, including their first update.
        // Work is bounded by the authored count; a hitch never creates an unbounded emission backlog.
        int due = (int)Math.Clamp(Math.Floor((_scriptAge - script.Delay) * script.EmitRate), 0, script.TotalCount);
        int expired = (int)Math.Clamp(Math.Floor((_scriptAge - script.Delay - script.Lifetime.Max.X) * script.EmitRate), 0, due);
        _scriptBirths = Math.Max(_scriptBirths, expired);
        while (_emitting && _scriptBirths < due)
        {
            double birth = script.Delay + (_scriptBirths + 1) / (double)script.EmitRate;
            if (birth > _scriptAge)
            {
                break;
            }

            _scriptBirths++;
            if (_aliveCount >= _particles.Length)
            {
                _scriptBirths = due;
                break;
            }

            var particle = new Particle { Age = (float)(_scriptAge - birth) };
            float fraction = dt > 0 ? MathHelper.Clamp((float)((birth - previousAge) / dt), 0, 1) : 1;
            var birthOrigin = Vector3.Lerp(_previousOrigin, origin, fraction);
            script.Initialize(ref particle, birthOrigin, unchecked(_scriptSeed + (uint)_scriptBirths * 2654435761u), _orientation);
            if (particle.Age < particle.Lifetime)
            {
                script.Evaluate(ref particle, _orientation);
                _particles[_aliveCount++] = particle;
            }
        }

        _previousOrigin = origin;
        if (_scriptBirths == script.TotalCount)
        {
            _emitting = false;
        }
    }

    public EmitterRenderView GetRenderView()
    {
        Texture2D? texture = null;
        var frames = _definition.Frames;
        if (frames.Count > 0)
        {
            texture = frames[0];
        }

        return new EmitterRenderView(_particles.AsSpan(0, _aliveCount), _definition, texture, _definition.Blend);
    }

    private void ApplyAnimation(ref Particle particle)
    {
        if (_definition.Animation is not { } animation)
        {
            return;
        }

        var exitAt = _definition.PotionMotion ? particle.MotionDuration : float.PositiveInfinity;
        var sample = animation.Sample(particle.Age, exitAt);
        particle.Frame = sample.Image;
        particle.Color.A = (byte)Math.Clamp((int)(particle.Color.A * sample.Alpha), 0, 255);
    }

    private int ResolveFrame(in Particle p, float life01)
    {
        var count = _definition.Frames.Count;
        if (count <= 1)
        {
            return 0;
        }

        if (_definition.FrameByLifetime)
        {
            var idx = (int)(life01 * count);
            return idx >= count ? count - 1 : idx;
        }

        if (_definition.FramesPerSecond <= 0f)
        {
            return 0;
        }

        var raw = (int)(p.Age * _definition.FramesPerSecond);
        return _definition.LoopFrames ? raw % count : (raw >= count ? count - 1 : raw);
    }

    private void Spawn(Vector3 origin)
    {
        if (_aliveCount >= _particles.Length)
            return;

        var seed01 = (float)_random.NextDouble();
        var lifetime = _definition.Lifetime.Sample((float)_random.NextDouble());
        var speed = _definition.InitialSpeed.Sample((float)_random.NextDouble());
        var radius = _definition.SpawnRadius.Sample((float)_random.NextDouble());

        var dir = RandomConeDirection(_definition.EmitDirection, _definition.DirectionSpreadRadians);
        var spawnOffset = radius > 0f ? RandomUnitVector() * radius : Vector3.Zero;

        ref var p = ref _particles[_aliveCount];
        p.Position = origin + spawnOffset;
        p.Origin = p.Position;
        p.Velocity = dir * speed;
        p.Age = 0f;
        p.Lifetime = lifetime <= 0f ? 0.0001f : lifetime;
        p.Rotation = _definition.InitialRotation.Sample((float)_random.NextDouble());
        p.RotationSpeed = _definition.RotationSpeed.Sample((float)_random.NextDouble());
        p.Seed01 = seed01;
        p.Color = _definition.Color.At(0f);
        p.Size = _definition.Size.At(0f);
        p.Frame = 0;
        if (_definition.PotionMotion)
        {
            PotionParticleMotion.Initialize(ref p, _random, _definition.Animation?.ExitDuration ?? 0f);
        }

        ApplyAnimation(ref p);
        _aliveCount++;
    }

    private Vector3 RandomConeDirection(Vector3 axis, float spreadRadians)
    {
        var a = axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : Vector3.Up;
        if (spreadRadians <= 0f)
        {
            return a;
        }

        var cosSpread = (float)Math.Cos(spreadRadians);
        var cosTheta = 1f - (float)_random.NextDouble() * (1f - cosSpread);
        var sinTheta = (float)Math.Sqrt(Math.Max(0f, 1f - cosTheta * cosTheta));
        var phi = (float)(_random.NextDouble() * Math.PI * 2.0);

        var helper = Math.Abs(a.Y) < 0.999f ? Vector3.Up : Vector3.Right;
        var tangent = Vector3.Normalize(Vector3.Cross(helper, a));
        var bitangent = Vector3.Cross(a, tangent);

        return tangent * (sinTheta * (float)Math.Cos(phi))
             + bitangent * (sinTheta * (float)Math.Sin(phi))
             + a * cosTheta;
    }

    private Vector3 RandomUnitVector()
    {
        var z = 1f - 2f * (float)_random.NextDouble();
        var r = (float)Math.Sqrt(Math.Max(0f, 1f - z * z));
        var phi = (float)(_random.NextDouble() * Math.PI * 2.0);
        return new Vector3(r * (float)Math.Cos(phi), r * (float)Math.Sin(phi), z);
    }
}
