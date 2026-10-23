using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Quests;

// Item checks and changes quests make on the carried items: counts by code, removal by code or identity, and grants
// that either fit completely or leave the pack untouched.
public static class QuestInventory
{
    public static int Count(PlayerCharacter character, uint code)
    {
        var total = 0;
        foreach (var item in character.OwnedItems())
        {
            if (item.Definition.Code == code)
            {
                total += item.Count;
            }
        }

        return total;
    }

    public static bool Has(PlayerCharacter character, uint code) => Count(character, code) > 0;

    public static bool Holds(PlayerCharacter character, Guid instanceId) => instanceId != Guid.Empty && character.FindOwnedItem(instanceId) is not null;

    // Removes up to amount units of the code from the pack and the equipment; returns how many were removed.
    public static int Remove(PlayerCharacter character, uint code, int amount = int.MaxValue)
    {
        var removed = 0;
        var inventory = character.Inventory;
        for (var anchor = 0; anchor < inventory.Capacity && removed < amount; anchor++)
        {
            if (inventory[anchor] is not { } item || item.Definition.Code != code)
            {
                continue;
            }

            var take = Math.Min(item.Count, amount - removed);
            if (take == item.Count)
            {
                inventory.TakeAt(anchor);
            }
            else
            {
                item.Count -= take;
                character.TouchInventory();
            }

            removed += take;
        }

        foreach (var pair in character.Equipment.ToArray())
        {
            if (removed < amount && pair.Value.Definition.Code == code)
            {
                character.Equip(pair.Key, null);
                removed++;
            }
        }

        return removed;
    }

    public static bool RemoveInstance(PlayerCharacter character, Guid instanceId)
    {
        if (instanceId == Guid.Empty)
        {
            return false;
        }

        var anchor = character.Inventory.Find(instanceId);
        if (anchor >= 0)
        {
            character.Inventory.TakeAt(anchor);
            return true;
        }

        foreach (var pair in character.Equipment.ToArray())
        {
            if (pair.Value.InstanceId == instanceId)
            {
                character.Equip(pair.Key, null);
                return true;
            }
        }

        return false;
    }

    // Places every item or none: the placement is tried on a copy of the pack first.
    public static bool TryGiveAll(PlayerCharacter character, IReadOnlyList<ItemInstance> items)
    {
        if (items.Count == 0)
        {
            return true;
        }

        var trial = character.CloneForInventoryOperation();
        foreach (var item in items)
        {
            if (item is null || !trial.Inventory.TryAdd(item.Copy(item.Count, preserveId: true)))
            {
                return false;
            }
        }

        foreach (var item in items)
        {
            character.Inventory.TryAdd(item);
        }

        return true;
    }

    // Creates and places the items of a reward list; the codes missing from the catalog are skipped.
    public static bool TryGrant(QuestContext context, IReadOnlyList<QuestGrant> grants)
    {
        var created = new List<ItemInstance>(grants.Count);
        foreach (var grant in grants)
        {
            var spec = grant.ClassSpecific ? QuestClasses.JobMask(context.Character) : 0u;
            for (var copy = 0; copy < grant.Copies; copy++)
            {
                if (grant.QuestVariant)
                {
                    if (context.Items.TryCreateQuestVariant(grant.Code, spec, out var variant))
                    {
                        created.Add(variant);
                    }

                    continue;
                }

                if (context.Items.TryCreate(grant.Code, grant.Count, spec, out var stack))
                {
                    created.Add(stack);
                    continue;
                }

                // An item that does not stack that far comes as single units.
                for (var unit = 0; unit < grant.Count && grant.Count > 1; unit++)
                {
                    if (context.Items.TryCreate(grant.Code, 1, spec, out var single))
                    {
                        created.Add(single);
                    }
                }
            }
        }

        return TryGiveAll(context.Character, created);
    }
}

// One reward line: Count units in each of Copies items, optionally specialised for the class or of the quest variant.
public readonly record struct QuestGrant(uint Code, int Count = 1, int Copies = 1, bool ClassSpecific = false, bool QuestVariant = false);
