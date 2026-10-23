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
        // The attack packet carries a reach of 48 in 8.8 fixed point (48 * 256), and the client compares it with a
        // squared distance. Preserve that effective reach, rather than treating 48 as a radius or adding a guessed pad.
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

    // Sound families whose basic attack plays as ranged; IDs are the game data's sound codes, not model filename guesses.
    public static bool HasRangedBasicAttack(int soundCode) => soundCode switch
    {
        0x1020 => true, // Imp
        0x10D0 => true, // Scorpion
        0x1120 => true, // Web
        0x1180 => true, // Figon
        0x11C0 => true, // Skeleton Ranger
        0x1240 => true, // Beedog
        0x1270 => true, // Mutant Tree
        0x1290 => true, // Naz
        0x12C0 => true, // Succubus
        0x12E0 => true, // Shadow
        0x12F0 => true, // Berserker
        0x1350 => true, // Stygian Lord
        0x1370 => true, // D-Machine
        0x1530 => true, // Hyper Machine
        0x1580 => true, // Runic Guardian
        0x1590 => true, // Sadness
        0x15E0 => true, // Avelin Archer
        0x15F0 => true, // Avelin Queen
        0x1600 => true, // Babel
        0x1630 => true, // Cold Eye
        0x1640 => true, // Frozen
        0x1670 => true, // Chaos Cara
        0x1720 => true, // M-Lord
        0x1800 => true, // Turtle Cannon
        0x1830 => true, // Kelvezu
        0x1870 => true, // Fire Worm
        0x1890 => true, // Dark Guard
        0x18A0 => true, // Dark Mage
        0x18D0 => true, // Seto
        0x19E0 => true, // Goblin Shaman
        0x2020 => true, // skill master NPC
        0x2030 => true, // magic master NPC
        0x2110 => true, // Naz Senior
        0x5020 or 0x5021 or 0x5022 => true, // castle crystals R/G/B
        _ => false
    };
}
