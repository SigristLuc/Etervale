using System.Numerics;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Simulation.Combat;

public static class MonsterOffensiveSkill
{
    // Original sound-family identifiers. Other families may have curses, summons or distinct impact rules.
    private const int DarkPhalanx = 0x1840;
    private const int DarkGuard = 0x1890;
    private const int DarkMage = 0x18A0;
    private const int KingSpider = 0x18E0;
    private const int Hest = 0x19F0;
    private const double ConnectedDistanceSquared = 0x120000;

    public static bool IsSupported(MonsterDefinition definition)
    {
        var skill = definition.Skill;
        return definition.SoundCode is DarkPhalanx or DarkGuard or DarkMage or KingSpider or Hest
            && skill.CurseSkillCode == 0
            && skill.SkillRatingPercent > 0
            && skill.SkillDamage.Min >= 0
            && skill.SkillDamage.Max > 0
            && skill.SkillDamage.Max >= skill.SkillDamage.Min
            && skill.SkillRange >= 0
            && skill.SkillDistance >= 0;
    }

    public static Vector3 AreaCenter(Vector3 origin, float facing, MonsterSkillStats skill)
    {
        if (skill.SkillRange == 0 || skill.SkillDistance == 0)
        {
            return origin;
        }

        var forward = new Vector3(MathF.Sin(facing), 0f, MathF.Cos(facing));
        return origin + forward * skill.SkillDistance;
    }

    public static float QueryRadius(MonsterSkillStats skill)
    {
        // Query around the caster. Include forward square corners, bounded by the original connected-player distance.
        float extent = skill.SkillRange;
        if (skill.SkillDistance > 0)
        {
            extent = skill.SkillDistance + skill.SkillRange * MathF.Sqrt(2f);
        }

        return MathF.Min(extent, MathF.Sqrt((float)ConnectedDistanceSquared));
    }

    public static bool Contains(MonsterSkillStats skill, Vector3 origin, Vector3 center, Vector3 target)
    {
        double dx = (double)target.X - origin.X;
        double dy = (double)target.Y - origin.Y;
        double dz = (double)target.Z - origin.Z;
        double distanceSquared = dx * dx + dy * dy + dz * dz;
        if (Math.Abs(dy) >= 140 || distanceSquared >= ConnectedDistanceSquared)
        {
            return false;
        }

        if (skill.SkillRange == 0)
        {
            // Explicit bounded single-target reach. Do not preserve the legacy packet's act=1 distance bypass.
            return distanceSquared < MonsterAttackReach.RangedImpactDistanceSquared;
        }

        if (skill.SkillDistance > 0)
        {
            return Math.Abs((double)target.X - center.X) < skill.SkillRange
                && Math.Abs((double)target.Z - center.Z) < skill.SkillRange;
        }

        return distanceSquared < (double)skill.SkillRange * skill.SkillRange;
    }
}
