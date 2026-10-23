using Microsoft.Xna.Framework;
using PristonTale.Effects;
using PristonTale.Effects.Effects;
using PristonTale.Effects.Vfx;

namespace PristonTale.Gameplay.Effects;

// A running original particle script: its source can move (particles already born stay) and it can be stopped (the
// live particles play out).
public interface IScriptEffect
{
    void MoveTo(Vector3 position);

    void Stop();
}

// Starts original particle scripts by name: in the game the converted effects ("particle.<name>" under Effect/Vfx), in
// the opening cinematic the legacy engine reading the scripts directly.
public interface IScriptEffects
{
    IScriptEffect Spawn(string script, Vector3 position, Matrix orientation);
}

public sealed class VfxScriptEffects(Func<SkillPresentation> presentation) : IScriptEffects
{
    public IScriptEffect Spawn(string script, Vector3 position, Matrix orientation)
    {
        var instance = presentation()?.SpawnAt(ParticlePrefix + script, position, orientation);
        return instance is null ? null : new Handle(instance);
    }

    public const string ParticlePrefix = "particle.";

    private sealed class Handle(VfxInstance instance) : IScriptEffect
    {
        public void MoveTo(Vector3 position) => instance.MoveTo(position);

        public void Stop() => instance.Stop();
    }
}

public sealed class LegacyScriptEffects(LegacyEffectLibrary library, EffectsEngine engine, string assetRoot) : IScriptEffects
{
    // The legacy engine stops spawning beyond this many live effects.
    public const int Budget = 128;

    public IScriptEffect Spawn(string script, Vector3 position, Matrix orientation)
    {
        if (engine.Instances.Count >= Budget || library.ParticleScript(assetRoot, script) is not { } definition)
        {
            return null;
        }

        return new Handle(engine.Spawn(definition, position, orientation));
    }

    private sealed class Handle(EffectInstance instance) : IScriptEffect
    {
        public void MoveTo(Vector3 position) => instance.Position = position;

        public void Stop() => instance.Stop();
    }
}
