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

// Starts original particle scripts by name: the converted effects ("particle.<name>" under Effect/Vfx) of the game's skill
// presentation, of a scene's own effect system (the opening flight), or the original engine reading the scripts directly.
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

public sealed class SystemScriptEffects(VfxSystem system, VfxLibrary library) : IScriptEffects
{
    public IScriptEffect Spawn(string script, Vector3 position, Matrix orientation)
    {
        if (!library.TryGet(VfxScriptEffects.ParticlePrefix + script, out var effect) || system.Spawn(effect, new VfxSpawn(0, 0, position, 0f)) is not { } instance)
        {
            return null;
        }

        instance.SetOrientation(orientation);
        return new Handle(instance);
    }

    private sealed class Handle(VfxInstance instance) : IScriptEffect
    {
        public void MoveTo(Vector3 position) => instance.MoveTo(position);

        public void Stop() => instance.Stop();
    }
}

public sealed class EngineScriptEffects(EffectLibrary library, EffectsEngine engine, string assetRoot) : IScriptEffects
{
    // The original engine stops spawning beyond this many live effects.
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
