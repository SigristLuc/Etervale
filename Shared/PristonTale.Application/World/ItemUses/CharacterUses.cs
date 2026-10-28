using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Application.World.ItemUses;

// Items whose effect is the character rules alone (UsableItemEffects): premium timers, resets, masteries and the
// gravity stone. They differ only in the line the player reads and the effect the world shows.

internal sealed class PremiumTimersUse : ItemUseAction
{
    // A timer that keeps counting offline is drunk where everyone sees it.
    public override ItemUseEffect Apply(in ItemUseInWorld use) =>
        use.Rule.Grants.Any(static grant => PremiumTimers.PausesOffline(grant.Kind)) ? new ItemUseEffect(ItemEffectKind.Drink) : ItemUseEffect.None;
}

internal sealed class ResetAttributesUse : ItemUseAction
{
    public override string Message(ItemDefinition item) => "Your stat points can be distributed again";
}

internal sealed class ResetSkillsUse : ItemUseAction
{
    public override string Message(ItemDefinition item) => "Your skill points can be distributed again";
}

internal sealed class SkillMasteryUse : ItemUseAction
{
    public override string Message(ItemDefinition item) => "Skill training is complete";

    public override ItemUseEffect Apply(in ItemUseInWorld use) => new(ItemEffectKind.Mastery);
}

// The aged item matures once, on the candidate; the commit takes the candidate's inventory as it is.
internal sealed class AgingMasteryUse : ItemUseAction
{
    public override bool ReappliesAtCommit => false;

    public override string Message(ItemDefinition item) => "The aged item has matured";
}

internal sealed class GravityScrollUse : ItemUseAction
{
    public override string Message(ItemDefinition item) => "You used a Gravity Stone";
}
