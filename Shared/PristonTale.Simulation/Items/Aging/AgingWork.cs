using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Items.Aging;

// One aging at the NPC, on a candidate copy of the character: the item from the inventory, exactly the sheltoms its
// level asks and at most one aging stone, all from the inventory, and the price in gold. The materials and the price are
// spent whatever the roll gives; a lost item leaves the inventory.
public static class AgingWork
{
    public static NpcServiceStatus TryAge(PlayerCharacter candidate, Guid itemId, IReadOnlyList<Guid> materials, ISimRng rng,
        out ItemWorkResult work, out long price)
    {
        work = null;
        price = 0;
        var inventory = candidate.Inventory;
        var anchor = inventory.Find(itemId);
        if (anchor < 0 || inventory[anchor] is not { } item || !AgingRules.Ageable(item))
        {
            return NpcServiceStatus.Rejected;
        }

        if (materials is null || materials.Count == 0 || materials.Count > NpcServiceCommand.MaxMaterials)
        {
            return NpcServiceStatus.WrongMaterials;
        }

        var ordinals = new List<int>();
        var stone = AgingStone.None;
        foreach (var group in materials.GroupBy(static id => id))
        {
            var at = inventory.Find(group.Key);
            if (group.Key == itemId || at < 0 || inventory[at] is not { } material || material.Count < group.Count())
            {
                return NpcServiceStatus.WrongMaterials;
            }

            var key = material.Definition.CatalogKey;
            var ordinal = AgingRules.SheltomOrdinal(key);
            if (ordinal > 0)
            {
                ordinals.AddRange(Enumerable.Repeat(ordinal, group.Count()));
                continue;
            }

            var kind = AgingRules.StoneOf(key);
            if (kind == AgingStone.None || stone != AgingStone.None || group.Count() != 1)
            {
                return NpcServiceStatus.WrongMaterials;
            }

            stone = kind;
        }

        if (!AgingRules.SheltomsMatch(item.UpgradeLevel, ordinals))
        {
            return NpcServiceStatus.WrongMaterials;
        }

        price = AgingRules.Price(item);
        if (!candidate.TryChangeMoney(-price))
        {
            return NpcServiceStatus.NotEnoughGold;
        }

        foreach (var id in materials)
        {
            inventory.ConsumeAt(inventory.Find(id));
        }

        var result = AgingRules.Roll(item.UpgradeLevel, stone, rng);
        var code = item.Definition.Code;
        if (result == AgingResult.Destroyed)
        {
            inventory.TakeAt(anchor);
            work = new ItemWorkResult((byte)result, Guid.Empty, code);
            return NpcServiceStatus.Ok;
        }

        AgingRules.Apply(item, result, stone);
        work = new ItemWorkResult((byte)result, item.InstanceId, code);
        return NpcServiceStatus.Ok;
    }
}
