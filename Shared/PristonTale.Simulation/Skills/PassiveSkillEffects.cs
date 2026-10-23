using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;

namespace PristonTale.Simulation.Skills;

// Passive skills whose rule carries a Passive block feed the derived stats as permanent effects of the character's
// effect container, so they take the same recompute path as items and buffs. The effect follows the learned rank and,
// for weapon passives, the wielded weapon.
public static class PassiveSkillEffects
{
    // Brings the passive effects in line with the learned ranks and the equipment; unchanged effects are left alone, so
    // the container's revision only moves when a passive really changes.
    public static void Sync(PlayerCharacter character)
    {
        foreach (var skill in character.Class.Skills)
        {
            if (skill.Code == 0 || !SkillRuleCatalog.TryGet(skill.Code, out var rule) || rule.Passive is not { Modifiers.Length: > 0 } passive)
            {
                continue;
            }

            var rank = character.Skills.RankOf(skill.Code);
            var active = rank > 0 && WeaponMatches(character, passive);
            var hasEffect = character.Effects.TryGet(skill.Code, out var current);
            if (!active)
            {
                if (hasEffect)
                {
                    character.Effects.Remove(skill.Code, 0, null);
                }

                continue;
            }

            var modifiers = new StatModifier[passive.Modifiers.Length];
            for (var index = 0; index < modifiers.Length; index++)
            {
                var entry = passive.Modifiers[index];
                modifiers[index] = new StatModifier(entry.Stat, entry.Op, entry.Values[rank - 1]);
            }

            if (hasEffect && current.Permanent && current.Spec.Modifiers.SequenceEqual(modifiers))
            {
                continue;
            }

            var spec = new EffectSpec(skill.Code, EffectSourceKind.PlayerSkill, false, modifiers);
            character.Effects.Apply(spec, 0, float.PositiveInfinity);
        }
    }

    // A weapon passive's share of the wielded weapon's own damage, truncated per endpoint like the original masteries.
    public static (int Min, int Max) WeaponBonus(PlayerCharacter character, ItemInstance weapon)
    {
        if (weapon is null || weapon.IsBroken)
        {
            return default;
        }

        var percent = character.Effects.Totals.Flat(StatKind.WeaponDamagePercent);
        if (percent == 0)
        {
            return default;
        }

        var stats = weapon.TotalStats;
        return ((int)(percent / 100f * stats.DamageMin), (int)(percent / 100f * stats.DamageMax));
    }

    private static bool WeaponMatches(PlayerCharacter character, SkillPassiveRule passive)
    {
        if (passive.Weapons.Length == 0)
        {
            return true;
        }

        var weapon = character.GetEquipped(EquipmentSlot.Weapon);
        return weapon is { IsBroken: false } && passive.Weapons.Contains(weapon.Definition.Kind);
    }
}
