using Microsoft.Xna.Framework;
using PristonTale.Effects.Particles;

namespace PristonTale.Effects.Effects;

public sealed class EffectInstance
{
    private readonly EffectDefinition _definition;
    private readonly ParticleEmitter[] _emitters;
    private readonly Vector3[] _offsets;
    private float _age;
    private bool _stopped;

    public EffectInstance(EffectDefinition definition, Vector3 position, int baseSeed, Matrix? orientation = null)
    {
        _definition = definition;
        Position = position;

        var count = definition.Emitters.Count;
        _emitters = new ParticleEmitter[count];
        _offsets = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            _offsets[i] = i < definition.EmitterOffsets.Count ? definition.EmitterOffsets[i] : Vector3.Zero;
            // Distinct, deterministic seed per emitter so they don't all draw the same random stream.
            _emitters[i] = new ParticleEmitter(definition.Emitters[i], position + _offsets[i], unchecked(baseSeed * 31 + i + 1), orientation);
        }
    }

    public EffectDefinition Definition => _definition;

    public Vector3 Position { get; set; }

    public IReadOnlyList<ParticleEmitter> Emitters => _emitters;

    public bool IsAlive
    {
        get
        {
            foreach (var e in _emitters)
            {
                if (e.IsAlive)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public void Stop()
    {
        if (_stopped)
        {
            return;
        }
        _stopped = true;
        foreach (var e in _emitters)
        {
            e.Stop();
        }
    }

    public void Update(float dt)
    {
        if (dt < 0f)
        {
            dt = 0f;
        }

        if (!_stopped && !_definition.Loop && _definition.Duration > 0f)
        {
            _age += dt;
            if (_age >= _definition.Duration)
            {
                Stop();
            }
        }

        for (var i = 0; i < _emitters.Length; i++)
        {
            _emitters[i].Update(dt, Position + _offsets[i]);
        }
    }
}
