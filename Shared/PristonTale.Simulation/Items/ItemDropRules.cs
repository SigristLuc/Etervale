using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Items;

// The items a player cannot put on the ground, as the original refuses them: quest items, the weapon and ring a quest
// holds, and the premium, event and time-limited items below (by catalog-key group, then by key).
public static class ItemDropRules
{
    private static readonly string[] Groups = ["MA1", "MA2", "QW1", "BI1", "BI2"];
    private static readonly HashSet<string> Keys = BuildKeys();

    public static bool CanDrop(PlayerCharacter character, ItemInstance item) =>
        CanDrop(item.Definition.Kind, item.Definition.CatalogKey) && !RankUpByQuestWeapon.IsBound(character, item);

    // The rule a view can check from the item's kind and key (the quest's own items are the server's to refuse).
    public static bool CanDrop(ItemKind kind, string key) =>
        kind != ItemKind.Quest && !Keys.Contains(key) && !Groups.Any(group => key.StartsWith(group, StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> BuildKeys()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "QT107", "QT108", "GF101", "GF107", "GF108", "OR233", "OA136", "OA137" };
        foreach (var number in Enumerable.Range(31, 16).Concat([54, 55]))
        {
            keys.Add($"DA1{number}");
            keys.Add($"DA2{number}");
        }

        foreach (var number in Enumerable.Range(31, 4))
        {
            keys.Add($"OA2{number}");
            keys.Add($"DB1{number}");
        }

        return keys;
    }
}
