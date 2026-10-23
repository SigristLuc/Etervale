using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.World.Npcs;

// Account storage kept by warehouse NPCs: the original 9x9 grid of 22-pixel cells, at most 100 items and 1000 weight.
// Quest items and crafting materials stay with the character, as in the original.
public sealed class WarehouseStorage
{
    public const int Columns = 9;
    public const int Rows = 9;
    public const int MaxItems = 100;
    public const int MaxWeight = 1000;
    public const long MaxMoney = 100_000_000;

    public WarehouseStorage(long revision = 0, long money = 0)
    {
        if (revision < 0 || money < 0 || money > MaxMoney)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        Revision = revision;
        Money = money;
    }

    public Inventory Grid { get; } = new(Columns, Rows);
    public long Money { get; private set; }
    public long Revision { get; private set; }

    public int Weight
    {
        get
        {
            var weight = 0;
            foreach (var item in Items())
            {
                weight += ItemWeight(item);
            }

            return weight;
        }
    }

    public int Count => Items().Count();

    public IEnumerable<ItemInstance> Items()
    {
        for (var anchor = 0; anchor < Grid.Capacity; anchor++)
        {
            if (Grid[anchor] is { } item)
            {
                yield return item;
            }
        }
    }

    public static bool CanStore(ItemInstance item) => item.Definition.Kind is not (ItemKind.Quest or ItemKind.Material);

    public static int ItemWeight(ItemInstance item) => Math.Max(0, item.Definition.Weight) * item.Count;

    public WarehouseStorage Clone()
    {
        var clone = new WarehouseStorage(Revision, Money);
        for (var anchor = 0; anchor < Grid.Capacity; anchor++)
        {
            if (Grid[anchor] is { } item)
            {
                clone.Grid.TryPlace(item.Copy(item.Count, preserveId: true), anchor);
            }
        }

        return clone;
    }

    public bool Restore(ItemInstance item, int anchor) => Count < MaxItems && Grid.TryPlace(item, anchor);

    public NpcServiceStatus TryDeposit(PlayerCharacter character, Guid itemId, int anchor)
    {
        var source = character.Inventory.Find(itemId);
        if (source < 0 || character.Inventory[source] is not { } item || !CanStore(item) || Quests.RankUpByQuestWeapon.IsBound(character, item))
        {
            return NpcServiceStatus.Rejected;
        }

        if (Weight + ItemWeight(item) > MaxWeight || Count >= MaxItems)
        {
            return NpcServiceStatus.NoSpace;
        }

        var stored = anchor >= 0 ? Grid.TryPlace(item, anchor) : Grid.TryAdd(item);
        if (!stored)
        {
            return NpcServiceStatus.NoSpace;
        }

        character.Inventory.TakeAt(source);
        Revision++;
        return NpcServiceStatus.Ok;
    }

    public NpcServiceStatus TryWithdraw(PlayerCharacter character, Guid itemId, int anchor)
    {
        var source = Grid.Find(itemId);
        if (source < 0 || Grid[source] is not { } item)
        {
            return NpcServiceStatus.Rejected;
        }

        if (!CarriedWeight.CanTake(character, CarriedWeight.Of(item)))
        {
            return NpcServiceStatus.OverWeight;
        }

        var placed = anchor >= 0 ? character.Inventory.TryPlace(item, anchor) : character.Inventory.TryAdd(item);
        if (!placed)
        {
            return NpcServiceStatus.NoSpace;
        }

        Grid.TakeAt(source);
        Revision++;
        return NpcServiceStatus.Ok;
    }

    public NpcServiceStatus TryMove(Guid itemId, int anchor)
    {
        var source = Grid.Find(itemId);
        if (source < 0 || !Grid.TryMove(source, anchor))
        {
            return NpcServiceStatus.Rejected;
        }

        Revision++;
        return NpcServiceStatus.Ok;
    }

    public NpcServiceStatus TryDepositGold(PlayerCharacter character, long amount)
    {
        if (amount <= 0 || amount > MaxMoney - Money)
        {
            return amount <= 0 ? NpcServiceStatus.Rejected : NpcServiceStatus.GoldLimit;
        }

        if (!character.TryChangeMoney(-amount))
        {
            return NpcServiceStatus.NotEnoughGold;
        }

        Money += amount;
        Revision++;
        character.TouchInventory();
        return NpcServiceStatus.Ok;
    }

    public NpcServiceStatus TryWithdrawGold(PlayerCharacter character, long amount, long carryLimit)
    {
        if (amount <= 0 || amount > Money)
        {
            return amount <= 0 ? NpcServiceStatus.Rejected : NpcServiceStatus.NotEnoughGold;
        }

        if (character.Money > carryLimit - amount || !character.TryChangeMoney(amount))
        {
            return NpcServiceStatus.GoldLimit;
        }

        Money -= amount;
        Revision++;
        character.TouchInventory();
        return NpcServiceStatus.Ok;
    }

    public WarehouseView View()
    {
        var items = new List<OwnedItemView>();
        for (var anchor = 0; anchor < Grid.Capacity; anchor++)
        {
            if (Grid[anchor] is { } item)
            {
                var definition = item.Definition;
                items.Add(new OwnedItemView(item.InstanceId, definition.Code, item.DisplayName, definition.Kind, item.Count, anchor, -1,
                    definition.GridWidth, definition.GridHeight, definition.CatalogKey));
            }
        }

        return new WarehouseView(Revision, Money, Weight, MaxWeight, Columns, Rows, items);
    }
}
