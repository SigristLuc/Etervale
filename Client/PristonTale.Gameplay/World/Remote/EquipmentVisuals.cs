using System.Globalization;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.World.Remote;

public static class EquipmentVisuals
{
    private static readonly Dictionary<string, (int Index, string Mesh, bool LeftHand, string VillageBone)> Items = BuildItems();

    private static Dictionary<string, (int, string, bool, string)> BuildItems()
    {
        var result = new Dictionary<string, (int, string, bool, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in EquipmentVisualTables.Items)
        {
            result.TryAdd(item.Key, (item.MotionIndex, item.Mesh, item.LeftHand, item.VillageBone));
        }

        return result;
    }

    public static IEnumerable<string> CatalogKeys => Items.Keys;

    public static int MotionIndex(string key) => Items.TryGetValue(key, out var item) ? item.Index : -1;

    public static string MeshPath(string key)
    {
        if (!Items.TryGetValue(key, out var item))
        {
            throw new InvalidDataException($"No original equipment resource for '{key}'.");
        }

        return $"image/sinimage/Items/DropItem/it{item.Mesh}.smd";
    }

    public static CharacterVisualSet Apply(CharacterVisualSet defaults, string armor, string weapon, string offHand, bool village, uint jobMask)
    {
        string body = defaults.Body.InxExtensionPath;
        if (!string.IsNullOrEmpty(armor))
        {
            if (armor.Length < 5 || !armor.StartsWith("DA", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(armor.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out int ordinal))
            {
                throw new InvalidDataException($"Invalid armor catalog key '{armor}'.");
            }

            int index = ordinal > 25 ? ordinal - 17 : ordinal;
            index = Math.Clamp(index, 0, 53);
            if ((uint)defaults.ArmorTableIndex >= (uint)EquipmentVisualTables.Armors.Length)
            {
                throw new InvalidDataException($"Invalid armor table index {defaults.ArmorTableIndex}.");
            }

            body = "char/tmABCD/" + EquipmentVisualTables.Armors[defaults.ArmorTableIndex][index];
        }

        return defaults with
        {
            Body = new CharacterAssetRef(body),
            WeaponKey = weapon ?? "",
            OffHandKey = offHand?.StartsWith("OM1", StringComparison.OrdinalIgnoreCase) == true ? "" : offHand ?? "",
            InVillage = village,
            JobMask = jobMask
        };
    }

    public static string WeaponBone(string key, bool village)
    {
        if (!Items.TryGetValue(key, out var item))
        {
            throw new InvalidDataException($"No original equipment hand for '{key}'.");
        }

        if (village && item.VillageBone.Length > 0)
        {
            return item.VillageBone;
        }

        return item.LeftHand ? "Bip01 L Hand" : "Bip weapon01";
    }
}
