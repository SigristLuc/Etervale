using System.Text.Json;
using System.Text.Json.Serialization;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;

namespace PristonTale.Persistence.Abstractions.Characters;

public sealed record SavedItem(
    Guid InstanceId,
    uint Code,
    int Count,
    int Anchor,
    EquipmentSlot? Slot,
    int UpgradeLevel,
    int DurabilityCurrent,
    int DurabilityMax,
    uint JobSpecMask,
    ItemStatBlock RolledBonus);

public sealed record SavedItems(int Version, SavedItem[] Items, long Revision = 0, AppliedInventoryOperation[]? Operations = null);
public sealed record SavedSkill(uint Code, int Rank);
public sealed record SavedSkills(int Version, int Points, int ElitePoints, SavedSkill[] Skills,
    long Revision = 0, AppliedProgressionOperation[]? Operations = null);

public static class CharacterStateCodec
{
    private static readonly JsonSerializerOptions Options = new(CharacterJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16
    };

    public static void Restore(PlayerCharacter character, CharacterRecord record, ItemCatalog catalog)
    {
        var inventory = ReadItems(record.Inventory, JsonValueKind.Array);
        var equipment = ReadEquipment(record.Equipment);
        var skills = ReadSkills(record.Skills);
        var identities = new HashSet<Guid>();
        character.RestoreProgress(record.Exp, record.Money, record.StatPoints, record.Tier, skills.Points, skills.ElitePoints);

        foreach (var saved in inventory.Items)
        {
            var item = RestoreItem(saved, catalog, identities);
            if (saved.Slot.HasValue || !character.Inventory.TryPlace(item, saved.Anchor))
            {
                throw new InvalidDataException("Invalid saved inventory footprint.");
            }
        }

        foreach (var saved in equipment.Items)
        {
            var item = RestoreItem(saved, catalog, identities);
            if (!saved.Slot.HasValue || saved.Anchor != -1 || character.GetEquipped(saved.Slot.Value) is not null
                || !character.CanEquip(saved.Slot.Value, item))
            {
                throw new InvalidDataException("Invalid saved equipment.");
            }

            character.Equip(saved.Slot.Value, item);
        }

        foreach (var skill in skills.Skills)
        {
            character.Skills.Restore(skill.Code, skill.Rank);
        }

        character.RestoreInventoryHistory(inventory.Revision, inventory.Operations ?? []);
        character.RestoreProgressionHistory(skills.Revision, skills.Operations ?? []);
    }

    public static SavedItems CaptureInventory(PlayerCharacter character)
    {
        var items = new List<SavedItem>();
        for (var anchor = 0; anchor < character.Inventory.Capacity; anchor++)
        {
            if (character.Inventory[anchor] is { } item)
            {
                items.Add(CaptureItem(item, anchor, null));
            }
        }

        return new SavedItems(1, items.ToArray(), character.InventoryRevision, character.InventoryOperations.ToArray());
    }

    public static SavedItems CaptureEquipment(PlayerCharacter character)
    {
        var items = new SavedItem[character.Equipment.Count];
        var index = 0;
        foreach (var equipped in character.Equipment)
        {
            items[index++] = CaptureItem(equipped.Value, -1, equipped.Key);
        }

        return new SavedItems(1, items);
    }

    public static SavedItems ReadEquipment(JsonText equipment)
    {
        return ReadItems(equipment, JsonValueKind.Object);
    }

    public static SavedSkills CaptureSkills(PlayerCharacter character)
    {
        var skills = new SavedSkill[character.Skills.Ranks.Count];
        var index = 0;
        foreach (var skill in character.Skills.Ranks)
        {
            skills[index++] = new SavedSkill(skill.Key, skill.Value);
        }

        return new SavedSkills(1, character.SkillPoints, character.EliteSkillPoints, skills,
            character.ProgressionRevision, character.ProgressionOperations.ToArray());
    }

    private static SavedItem CaptureItem(ItemInstance item, int anchor, EquipmentSlot? slot)
    {
        return new SavedItem(item.InstanceId, item.Definition.Code, item.Count, anchor, slot, item.UpgradeLevel,
            item.DurabilityCurrent, item.DurabilityMax, item.JobSpecMask, item.RolledBonus);
    }

    private static ItemInstance RestoreItem(SavedItem saved, ItemCatalog catalog, HashSet<Guid> identities)
    {
        if (saved is null || saved.InstanceId == Guid.Empty || !identities.Add(saved.InstanceId)
            || !catalog.TryGet(saved.Code, out var definition) || saved.Count < 1 || saved.Count > definition.MaxStack
            || saved.UpgradeLevel != 0 || saved.DurabilityCurrent < 0 || saved.DurabilityMax < saved.DurabilityCurrent)
        {
            throw new InvalidDataException("Invalid or unsupported saved item.");
        }

        return new ItemInstance(definition, saved.Count)
        {
            InstanceId = saved.InstanceId,
            UpgradeLevel = saved.UpgradeLevel,
            DurabilityCurrent = saved.DurabilityCurrent,
            DurabilityMax = saved.DurabilityMax,
            JobSpecMask = saved.JobSpecMask,
            RolledBonus = saved.RolledBonus
        };
    }

    private static SavedItems ReadItems(JsonText json, JsonValueKind legacyKind)
    {
        if (IsLegacyEmpty(json, legacyKind))
        {
            return new SavedItems(1, []);
        }

        var state = JsonSerializer.Deserialize<SavedItems>(json.Value, Options);
        if (state is null || state.Version != 1 || state.Items is null || state.Items.Length > 72)
        {
            throw new InvalidDataException("Unsupported item-state version or size.");
        }

        return state;
    }

    private static SavedSkills ReadSkills(JsonText json)
    {
        if (IsLegacyEmpty(json, JsonValueKind.Array))
        {
            return new SavedSkills(1, 0, 0, []);
        }

        var state = JsonSerializer.Deserialize<SavedSkills>(json.Value, Options);
        if (state is null || state.Version != 1 || state.Skills is null || state.Skills.Length > 128 || state.Points < 0 || state.ElitePoints < 0)
        {
            throw new InvalidDataException("Unsupported skill-state version or size.");
        }

        return state;
    }

    private static bool IsLegacyEmpty(JsonText json, JsonValueKind expected)
    {
        if (string.IsNullOrWhiteSpace(json.Value) || json.Value.Length > 65536)
        {
            throw new InvalidDataException("Invalid owned-state JSON size.");
        }

        using var document = JsonDocument.Parse(json.Value, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        return root.ValueKind == expected && (expected == JsonValueKind.Array
            ? root.GetArrayLength() == 0
            : !root.EnumerateObject().MoveNext());
    }
}
