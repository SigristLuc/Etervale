using System.Numerics;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Simulation.Combat;

public static class MonsterAttackReach
{
    public const float VerticalEntryLimit = 64f;
    public const float BasicImpactDistanceSquared = 48f * 256f;
    public const float RangedImpactDistanceSquared = 460f * 460f;

    public static bool CanBegin(MonsterDefinition monster, Vector3 position, Vector3 target)
    {
        var range = monster.Combat.MeleeRange;
        return range > 0
            && MathF.Abs(position.Y - target.Y) <= VerticalEntryLimit
            && PlanarDistanceSquared(position, target) < (double)range * range;
    }

    public static bool CanLandBasic(MonsterDefinition monster, Vector3 position, Vector3 target)
    {
        // The packet carries AttackSize=48*fONE and PlayAttack compares it to a SQUARED distance.
        // Preserve that effective reach, rather than treating 48 as a radius or adding a guessed pad.
        var limit = HasRangedBasicAttack(monster.SoundCode) ? RangedImpactDistanceSquared : BasicImpactDistanceSquared;
        return MathF.Abs(position.Y - target.Y) <= VerticalEntryLimit
            && PlanarDistanceSquared(position, target) < limit;
    }

    private static double PlanarDistanceSquared(Vector3 left, Vector3 right)
    {
        var x = (double)left.X - right.X;
        var z = (double)left.Z - right.Z;
        return x * x + z * z;
    }

    // Sound families the original attack playback treats as ranged; IDs are the original sound codes, not model filename guesses.
    public static bool HasRangedBasicAttack(int soundCode) => soundCode switch
    {
        0x1020 => true, // IMP
        0x10D0 => true, // SCORPION
        0x1120 => true, // WEB
        0x1180 => true, // FIGON
        0x11C0 => true, // SKELETONRANGE
        0x1240 => true, // BEEDOG
        0x1270 => true, // MUTANTTREE
        0x1290 => true, // NAZ
        0x12C0 => true, // SUCCUBUS
        0x12E0 => true, // SHADOW
        0x12F0 => true, // BERSERKER
        0x1350 => true, // STYGIANLORD
        0x1370 => true, // DMACHINE
        0x1530 => true, // HYPERMACHINE
        0x1580 => true, // RUNICGUARDIAN
        0x1590 => true, // SADNESS
        0x15E0 => true, // AVELIN_ARCHER
        0x15F0 => true, // AVELIN_QUEEN
        0x1600 => true, // BABEL
        0x1630 => true, // COLDEYE
        0x1640 => true, // FROZEN
        0x1670 => true, // CHAOSCARA
        0x1720 => true, // M_LORD
        0x1800 => true, // TURTLE_CANNON
        0x1830 => true, // KELVEZU
        0x1870 => true, // FIREWORM
        0x1890 => true, // DARKGUARD
        0x18A0 => true, // DARKMAGE
        0x18D0 => true, // SETO
        0x19E0 => true, // GOBLINSHAMAN
        0x2020 => true, // NPC_SKILLMASTER
        0x2030 => true, // NPC_MAGICMASTER
        0x2110 => true, // NAZSENIOR
        0x5020 or 0x5021 or 0x5022 => true, // CASTLE_CRYSTAL R/G/B
        _ => false
    };
}
