using System.Numerics;
using PristonTale.Simulation.Skills;

namespace PristonTale.Application.World;

// Revive skills: the nearest dead player within the skill's radius rises where it fell, with a share of its life and a
// short time in which it cannot be hurt, when the rank's chance succeeds.
internal sealed partial class FieldSkills
{
    private WorldPlayer? FindDeadAlly(WorldPlayer caster, int radius)
    {
        WorldPlayer? nearest = null;
        var best = radius * (float)radius;
        foreach (var player in _host.Players)
        {
            if (!player.IsDead || player.EntityId == caster.EntityId || MathF.Abs(player.Position.Y - caster.Position.Y) > radius)
            {
                continue;
            }

            var distance = new Vector2(player.Position.X - caster.Position.X, player.Position.Z - caster.Position.Z).LengthSquared();
            if (distance < best)
            {
                best = distance;
                nearest = player;
            }
        }

        return nearest;
    }

    private bool Resurrect(WorldPlayer caster, ActiveCast cast)
    {
        var resurrect = cast.Rule.Resurrect;
        if (!_host.TryGetPlayer(cast.TargetId, out var fallen) || !fallen.IsDead
            || _rng.RollPercent() >= SkillFormulas.RankValue(resurrect.Chance, cast.Rank))
        {
            return false;
        }

        fallen.Revive(fallen.MaxHp * resurrect.LifePercent / 100, resurrect.GuardTenths * 100f);
        _host.EmitCombatEvent(new CombatEventView(caster.EntityId, fallen.EntityId, CombatEventKindView.Revive, damage: fallen.Hp));
        return true;
    }
}
