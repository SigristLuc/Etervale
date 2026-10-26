using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Effects;
using PristonTale.Effects.Effects;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Gameplay.Effects;

// A running force orb lights its holder's weapon with flares in the orb's colour, and each hit the holder lands bursts
// the orb's spark on the target with a white light.
public sealed class ForceOrbPresentation
{
    private readonly SkillPresentation _skills;
    private readonly EffectsEngine _engine;
    private readonly PristonTransientLights _lights;
    private readonly EffectDefinition _hit;
    private readonly Dictionary<ulong, Glow> _active = new();

    public ForceOrbPresentation(SkillPresentation skills, EffectLibrary library, string assetRoot, EffectsEngine engine, PristonTransientLights lights)
    {
        _skills = skills;
        _engine = engine;
        _lights = lights;
        _hit = library?.ParticleScript(assetRoot, "ShellTomWeaponHit");
    }

    public int ActiveCount => _active.Count;

    public void Observe(PresenceUpdate update, Func<ulong, Vector3?> position, double nowMs)
    {
        foreach (var status in update.StatusEffects)
        {
            if (!ForceOrbs.TryGetByStatus(status.StatusCode, out var orb))
            {
                continue;
            }

            if (status.Op == StatusEffectOp.Remove)
            {
                Stop(status.EntityId);
            }
            else if (status.Op is StatusEffectOp.Add or StatusEffectOp.Refresh)
            {
                Stop(status.EntityId);
                _active[status.EntityId] = new Glow(orb.Index - 1, nowMs + status.RemainingMs);
            }
        }

        foreach (var hit in update.Events)
        {
            if (hit.Kind == RemoteCombatEventKind.Hit && _active.ContainsKey(hit.AttackerId) && position(hit.TargetId) is { } target)
            {
                var at = target + Vector3.Up * 22f;
                if (_hit is not null && _engine.Instances.Count < 128)
                {
                    _engine.Spawn(_hit, at);
                }

                _lights?.Add(at, new Color(255, 255, 255), 250, 1);
            }
        }
    }

    // Keeps a glow on every holder of a running orb; a glow lasts 30 s and is renewed until the orb ends.
    public void Update(double nowMs, Func<ulong, Vector3?> position)
    {
        foreach (var (entity, glow) in _active.ToArray())
        {
            if (glow.EndMs <= nowMs)
            {
                Stop(entity);
                continue;
            }

            if (glow.RenewMs <= nowMs && position(entity) is { } at && _skills is not null)
            {
                glow.Instance?.Stop();
                glow.Instance = _skills.Spawn(ItemVfx.OrbGlow(glow.Index), entity, entity, at);
                glow.RenewMs = nowMs + (ItemVfx.GlowSeconds - 1f) * 1000d;
            }
        }
    }

    private void Stop(ulong entity)
    {
        if (_active.Remove(entity, out var glow))
        {
            glow.Instance?.Stop();
        }
    }

    private sealed class Glow(int index, double endMs)
    {
        public int Index { get; } = index;
        public double EndMs { get; } = endMs;
        public double RenewMs { get; set; }
        public VfxInstance Instance { get; set; }
    }
}
