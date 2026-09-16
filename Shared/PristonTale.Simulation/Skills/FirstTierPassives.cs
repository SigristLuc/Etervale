using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Skills;

public static class FirstTierPassives
{
    private static ReadOnlySpan<int> PoisonAndFire => [9, 17, 24, 30, 35, 39, 42, 44, 45, 46];
    private static ReadOnlySpan<int> Ice => [8, 15, 21, 26, 30, 33, 36, 39, 42, 45];
    private static ReadOnlySpan<int> Melee => [6, 10, 14, 18, 21, 24, 26, 28, 30, 32];
    private static ReadOnlySpan<int> Shooting => [4, 8, 12, 16, 20, 24, 28, 32, 36, 40];
    private static ReadOnlySpan<int> Throwing => [18, 22, 26, 30, 34, 38, 41, 44, 47, 50];
    private static ReadOnlySpan<int> DualWield => [5, 7, 9, 11, 14, 17, 20, 23, 27, 31];
    private static ReadOnlySpan<int> Stamina => [5, 8, 11, 14, 17, 20, 23, 26, 28, 30];

    public static DerivedStats Apply(DerivedStats stats, LearnedSkills skills)
    {
        var percent = Value(Stamina, skills.RankOf(0x05010003));
        // Physical Training adds a truncated fraction of the already computed maximum, once.
        var bonus = (int)(percent * (float)stats.MaxStamina / 100f);
        return stats with { MaxStamina = stats.MaxStamina + bonus };
    }

    public static ResistanceBlock Resistances(LearnedSkills skills)
    {
        return new ResistanceBlock(0, 0,
            (short)Value(PoisonAndFire, skills.RankOf(0x02010002)),
            (short)Value(Ice, skills.RankOf(0x03010002)), 0,
            (short)Value(PoisonAndFire, skills.RankOf(0x01010004)), 0, 0);
    }

    public static (int Min, int Max) WeaponBonus(LearnedSkills skills, ItemInstance weapon)
    {
        if (weapon is null || weapon.IsBroken)
        {
            return default;
        }

        var percent = weapon.Definition.Kind switch
        {
            ItemKind.Axe or ItemKind.Claw or ItemKind.Hammer or ItemKind.Sword => Value(Melee, skills.RankOf(0x02010001)),
            ItemKind.Bow => Value(Shooting, skills.RankOf(0x04010002)),
            ItemKind.ThrowingWeapon => Value(Throwing, skills.RankOf(0x06010003)),
            ItemKind.Dagger => Value(DualWield, skills.RankOf(0x10010003)),
            _ => 0
        };
        var stats = weapon.TotalStats;
        // The source uses float division before multiplication and truncates each endpoint.
        // The percentage applies to the weapon's damage, not the attribute-scaled final attack.
        return ((int)(percent / 100f * stats.DamageMin), (int)(percent / 100f * stats.DamageMax));
    }

    private static int Value(ReadOnlySpan<int> values, int rank) => rank == 0 ? 0 : values[rank - 1];
}
