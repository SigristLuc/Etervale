using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Simulation.Combat;

public static class CombatProfileFactory
{
    // A normal attack also gains the normal-attack weapon buffs (a share of the weapon's own damage); skills do not.
    public static AttackProfile ForPlayerBasicAttack(PlayerCharacter character, bool normalAttack = false)
    {
        ArgumentNullException.ThrowIfNull(character);
        var damage = WeaponDamage.Compute(character);
        var totals = character.Effects.Totals;
        if (normalAttack && character.GetEquipped(EquipmentSlot.Weapon) is { IsBroken: false } weapon)
        {
            var stats = weapon.TotalStats;
            var both = totals.Flat(StatKind.NormalAttackWeaponDamage);
            var max = damage.Max + stats.DamageMax * (totals.Flat(StatKind.NormalAttackWeaponMax) + both) / 100;
            damage = (damage.Min + stats.DamageMin * both / 100, max);
        }

        var critical = Math.Max(0, totals.Flat(StatKind.CriticalChance) + PassiveSkills.CriticalChance(character));
        return new AttackProfile(character.Derived.AttackRating, damage.Min, damage.Max, character.Level, Element.Bionic, critical, 0, 0)
        {
            CritBonusPercent = totals.Flat(StatKind.CriticalDamage)
        };
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

    public static bool HasShield(PlayerCharacter character) =>
        character.GetEquipped(EquipmentSlot.OffHand) is { IsBroken: false } offHand && offHand.Definition.Kind == ItemKind.Shield;

    public static DefenderProfile ForPlayerDefender(PlayerCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        DerivedStats stats = character.Derived;
        return new DefenderProfile(
            Defence: stats.Defence,
            Absorption: stats.Absorption,
            // Item block ratings are not applied; block comes from skills and other sources through the modifier engine.
            BlockChance: Math.Max(0, character.Effects.Totals.Flat(StatKind.BlockChance) + PassiveSkills.BlockChance(character)),
            Level: character.Level,
            Resistances: ModifierRules.Resistances(character.Effects.Totals, PassiveSkills.Resistances(character.Skills)),
            MaxLife: character.MaxLife,
            CurrentLife: character.Life,
            IsMonster: false);
    }
}
