using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Modifiers;

namespace PristonTale.Simulation.Items.Usable;

// The premium timers that change a stat feed the effect container as permanent system effects, so skill costs and the
// damage scaling read them like any other modifier: Mana Reduce and Terry lower skill mana costs, Mute raises the damage
// its owner deals.
public static class PremiumEffects
{
    public const uint ManaReduceCode = 0xF0000101;
    public const uint MuteCode = 0xF0000102;

    public static void Sync(PlayerCharacter character)
    {
        var premium = character.Premium;
        var manaPercent = premium.ValueOf(PremiumEffectKind.ManaReduce)
            + (premium.IsActive(PremiumEffectKind.HelpPetTerry) ? PremiumRules.TerryManaReducePercent : 0);
        Set(character, ManaReduceCode, manaPercent > 0 ? [StatModifier.Percent(StatKind.SkillManaCost, -manaPercent)] : []);
        Set(character, MuteCode, premium.IsActive(PremiumEffectKind.HelpPetMute)
            ? [StatModifier.Percent(StatKind.DamageDealt, PremiumRules.MuteDamagePercent)]
            : []);
    }

    private static void Set(PlayerCharacter character, uint code, StatModifier[] modifiers)
    {
        var hasEffect = character.Effects.TryGet(code, out var current);
        if (modifiers.Length == 0)
        {
            if (hasEffect)
            {
                character.Effects.Remove(code, 0, null);
            }

            return;
        }

        if (hasEffect && current.Permanent && current.Spec.Modifiers.SequenceEqual(modifiers))
        {
            return;
        }

        character.Effects.Apply(new EffectSpec(code, EffectSourceKind.System, false, modifiers), 0, float.PositiveInfinity);
    }
}
