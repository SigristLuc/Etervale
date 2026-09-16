using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Simulation.Combat;

public static class CombatProfileFactory
{
    public static AttackProfile ForPlayerBasicAttack(PlayerCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var damage = WeaponDamage.Compute(character);
        return new AttackProfile(character.Derived.AttackRating, damage.Min, damage.Max,
            character.Level, Element.Bionic, 0, 0, 0);
    }

    public static AttackProfile ForMonsterBasicAttack(MonsterCombatStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return new AttackProfile(
            AttackRating: stats.AttackRatingAccuracy,
            DamageMin: stats.AttackDamage.Min,
            DamageMax: stats.AttackDamage.Max,
            AttackerLevel: stats.Level,
            Element: Element.Bionic,
            CritChance: 0,
            SkillMultiplierPercent: 0,
            SkillFlatAdd: 0);
    }

    public static DefenderProfile ForPlayerDefender(PlayerCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        DerivedStats stats = character.Derived;
        return new DefenderProfile(
            Defence: stats.Defence,
            Absorption: stats.Absorption,
            BlockChance: 0,               // player block accumulator un-ported (items/skills) — no block yet.
            Level: character.Level,
            Resistances: FirstTierPassives.Resistances(character.Skills),
            MaxLife: character.MaxLife,
            CurrentLife: character.Life,
            IsMonster: false);
    }
}
