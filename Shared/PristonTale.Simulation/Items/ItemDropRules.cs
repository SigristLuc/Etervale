using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Items;

// Which carried items a player may throw on the ground. Quest items, quest weapons and the running quest's ring, the
// premium and mix families and a list of costumes and event accessories stay in the bag.
public static class ItemDropRules
{
    public const string Refusal = "This item cannot be dropped";

    private static readonly string[] KeptFamilies = ["BI1", "BI2", "MA1", "MA2", "QW1"];

    private static readonly HashSet<string> KeptCodes = BuildKeptCodes();

    public static bool CanDrop(PlayerCharacter character, ItemInstance item)
    {
        if (item is null)
        {
            return false;
        }

        var key = item.Definition.CatalogKey ?? "";
        if (item.Definition.Kind == ItemKind.Quest || key.Contains('#', StringComparison.Ordinal) || KeptCodes.Contains(key)
            || KeptFamilies.Any(family => key.StartsWith(family, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var active = character.Quests.Active;
        return item.InstanceId != active.WeaponId && item.InstanceId != active.RingId;
    }

    private static HashSet<string> BuildKeptCodes()
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "QT107", "QT108", "GF101", "CA510", "CA512", "CA610", "CA612", "DA231", "DA154", "DA155", "DA254", "DA255",
            "OA231", "OA232", "OA233", "OA234", "DB131", "DB132", "DB133", "DB134", "OR233", "OA136", "OA137"
        };
        for (var number = 32; number <= 46; number++)
        {
            codes.Add($"DA1{number}");
            codes.Add($"DA2{number}");
        }

        return codes;
    }
}
