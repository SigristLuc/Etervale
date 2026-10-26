using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Items;

// What the owner sees of one item copy: its values, the fixed values of its kind and whether the owner's class may use
// it. Requirement checks against the owner's level and attributes are made where the view is shown.
public sealed record ItemDetailView(
    int DurabilityCurrent,
    int DurabilityMax,
    int UpgradeLevel,
    ItemRoll Roll,
    int Critical,
    int AttackSpeed,
    int ShootingRange,
    int PotionSpace,
    float MagicMastery,
    int Weight,
    int UniqueItem,
    bool TwoHanded,
    bool ClassAllowed)
{
    // The aging under way (count toward goal, 0 of 0 when none) and the mix's highest sheltom (0 never mixed).
    public int AgingCount { get; init; }
    public int AgingGoal { get; init; }
    public int MixSheltom { get; init; }

    public static ItemDetailView Of(ItemInstance item, PlayerCharacter owner)
    {
        var definition = item.Definition;
        return new ItemDetailView(item.DurabilityCurrent, item.DurabilityMax, item.UpgradeLevel, item.Roll, item.Critical,
            definition.BaseStats.AttackSpeed + item.Roll.AttackSpeedBonus, definition.ShootingRange, definition.PotionSpace, definition.MagicMastery,
            CarriedWeight.Of(definition, 1), definition.UniqueItem, definition.TwoHanded, ItemUsability.ClassAllowed(owner, definition))
        {
            AgingCount = item.Maturity?.Count ?? 0,
            AgingGoal = item.Maturity?.Goal ?? 0,
            MixSheltom = item.MixSheltom
        };
    }

    public bool MeetsLevel(int level) => level >= Roll.RequiredLevel;

    public bool Usable(int level, CharacterAttributes attributes) =>
        ClassAllowed && MeetsLevel(level)
        && attributes.Strength >= Roll.Requirements.Strength
        && attributes.Spirit >= Roll.Requirements.Spirit
        && attributes.Talent >= Roll.Requirements.Talent
        && attributes.Agility >= Roll.Requirements.Agility
        && attributes.Health >= Roll.Requirements.Health;
}
