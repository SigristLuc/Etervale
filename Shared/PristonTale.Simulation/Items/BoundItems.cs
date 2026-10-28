using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Items;

// The items the original never lets leave the character, neither on the ground nor in the warehouse: quest items, the
// weapon and ring a running rank-up holds, the crafting materials (MA1, MA2), the QW1 family and GF101.
public static class BoundItems
{
    private static readonly string[] Families = ["MA1", "MA2", "QW1"];

    public static bool IsBound(PlayerCharacter character, ItemInstance item) =>
        IsBound(item.Definition.Kind, item.Definition.CatalogKey) || RankUpByQuestWeapon.IsBound(character, item);

    // The rule a view can check from the item's kind and key (the quest's own items are the server's to refuse).
    public static bool IsBound(ItemKind kind, string key) =>
        kind == ItemKind.Quest || key.Equals("GF101", StringComparison.OrdinalIgnoreCase) || ItemCodes.InFamily(key, Families);
}
