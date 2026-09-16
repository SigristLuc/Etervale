using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Combat;

public static class WeaponDamage
{
    public static (int Min, int Max) Compute(PlayerCharacter character)
    {
        var weapon = character.GetEquipped(EquipmentSlot.Weapon);
        if (weapon is null || weapon.IsBroken)
        {
            return (character.Derived.AttackDamageMin, character.Derived.AttackDamageMax);
        }

        var attributes = character.Attributes;
        var function = character.Definition.FunctionProfile;
        var stats = weapon.TotalStats;
        int primary;
        int divisor;
        int secondary;
        if (weapon.Definition.Kind is ItemKind.Staff or ItemKind.Phantom && function.MagicDamageFunction > 0)
        {
            primary = attributes.Spirit;
            divisor = function.MagicDamageFunction == 1 ? 150 : 170;
            secondary = attributes.Talent / 30;
        }
        else if (weapon.Definition.Kind is ItemKind.Bow or ItemKind.ThrowingWeapon)
        {
            primary = attributes.Agility;
            divisor = function.RangedDamageFunction == 1 ? 130 : 190;
            secondary = (attributes.Talent + attributes.Strength) / (function.RangedDamageFunction == 1 ? 40 : 50);
        }
        else
        {
            primary = attributes.Strength;
            divisor = function.MeleeDamageFunction switch
            {
                1 => 130,
                2 => 150,
                _ => 190
            };
            var secondaryDivisor = function.MeleeDamageFunction switch
            {
                1 => 40,
                2 => 45,
                _ => 50
            };
            secondary = (attributes.Talent + attributes.Agility) / secondaryDivisor;
        }

        var favoured = character.Definition.Class switch
        {
            PlayerClass.Fighter => weapon.Definition.Kind == ItemKind.Axe,
            PlayerClass.Mechanician => (weapon.JobSpecMask & (uint)JobBitCode.Mechanician) != 0,
            PlayerClass.Archer => weapon.Definition.Kind == ItemKind.Bow,
            PlayerClass.Pikeman => weapon.Definition.Kind == ItemKind.Pole,
            PlayerClass.Knight => weapon.Definition.Kind == ItemKind.Sword,
            PlayerClass.Atalanta => weapon.Definition.Kind == ItemKind.ThrowingWeapon,
            PlayerClass.Assassin => weapon.Definition.Kind == ItemKind.Dagger,
            PlayerClass.Priestess or PlayerClass.Magician => weapon.Definition.Kind == ItemKind.Staff,
            PlayerClass.Shaman => weapon.Definition.Kind == ItemKind.Phantom,
            _ => false
        };
        var jobDamage = favoured ? character.Level / 6 : 0;
        var mastery = FirstTierPassives.WeaponBonus(character.Skills, weapon);
        var minimum = 2 + stats.DamageMin * (primary + divisor) / divisor + secondary + jobDamage + (stats.DamageMin + stats.DamageMax) / 16;
        var maximum = 4 + stats.DamageMax * (primary + divisor) / divisor + secondary + jobDamage;
        return (minimum + mastery.Min, maximum + mastery.Max);
    }
}
