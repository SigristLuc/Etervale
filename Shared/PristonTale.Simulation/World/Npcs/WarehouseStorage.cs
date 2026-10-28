using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.World.Npcs;

// Account storage kept by warehouse NPCs: the original 9x9 grid of 22-pixel cells, at most 100 items and 1000 weight.
public sealed class WarehouseStorage
{
    public const int Columns = 9;
    public const int Rows = 9;
    public const int MaxItems = 100;
    public const int MaxWeight = 1000;
    public const long MaxMoney = 100_000_000;

    // What the original keeps out besides BoundItems: potions, and the event (SP1) and CH1 families.
    private static readonly string[] Families = ["SP1", "CH1"];

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

    public static bool CanStore(PlayerCharacter character, ItemInstance item) =>
        !BoundItems.IsBound(character, item) && Storable(item.Definition.Kind, item.Definition.CatalogKey);

    // The rule a view can check from the item's kind and key (the quest's own items are the server's to refuse).
    public static bool CanStore(ItemKind kind, string key) => !BoundItems.IsBound(kind, key) && Storable(kind, key);

    // The original places a potion over the grid as any item and refuses it only when it is set down, with
    // ItemRuleMessages.PotionNotStored; the other items CanStore refuses show red where they would land.
    public static bool RefusedOnSetDown(ItemKind kind) => kind == ItemKind.Potion;

    private static bool Storable(ItemKind kind, string key) => !RefusedOnSetDown(kind) && !ItemCodes.InFamily(key, Families);

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

    // An item set down on the grid, as the original sets it: on free cells (-1: the first that fit), or over one stored
    // item, which goes to the item's bag cell (or wherever the bag has room) and is carried in turn. covered is the item
    // the client saw under it, or empty.
    public NpcServiceStatus TryDeposit(PlayerCharacter character, Guid itemId, int anchor, Guid covered = default)
    {
        var source = character.Inventory.Find(itemId);
        if (source < 0 || character.Inventory[source] is not { } item || !CanStore(character, item) || !TryCovered(Grid, anchor, item, covered, out var displaced))
        {
            return NpcServiceStatus.Rejected;
        }

        if (Weight - (displaced is null ? 0 : ItemWeight(displaced)) + ItemWeight(item) > MaxWeight || displaced is null && Count >= MaxItems)
        {
            return NpcServiceStatus.NoSpace;
        }

        if (displaced is not null && !CarriedWeight.CanTake(character, CarriedWeight.Of(displaced) - CarriedWeight.Of(item)))
        {
            return NpcServiceStatus.OverWeight;
        }

        return SetDown(character.Inventory, source, Grid, anchor, displaced) ? Changed() : NpcServiceStatus.NoSpace;
    }

    // A stored item set down in the bag: on free cells, or over one bag item, which goes to the stored item's cell (or
    // wherever the grid has room) and is carried in turn.
    public NpcServiceStatus TryWithdraw(PlayerCharacter character, Guid itemId, int anchor, Guid covered = default)
    {
        var source = Grid.Find(itemId);
        if (source < 0 || Grid[source] is not { } item || !TryCovered(character.Inventory, anchor, item, covered, out var displaced)
            || displaced is not null && !CanStore(character, displaced))
        {
            return NpcServiceStatus.Rejected;
        }

        if (!CarriedWeight.CanTake(character, CarriedWeight.Of(item) - (displaced is null ? 0 : CarriedWeight.Of(displaced))))
        {
            return NpcServiceStatus.OverWeight;
        }

        if (displaced is not null && Weight - ItemWeight(item) + ItemWeight(displaced) > MaxWeight)
        {
            return NpcServiceStatus.NoSpace;
        }

        return SetDown(Grid, source, character.Inventory, anchor, displaced) ? Changed() : NpcServiceStatus.NoSpace;
    }

    // A stored item set down elsewhere on the grid, over free cells or one other stored item (carried in turn).
    public NpcServiceStatus TryMove(Guid itemId, int anchor, Guid covered = default)
    {
        var source = Grid.Find(itemId);
        if (source < 0 || anchor < 0 || Grid[source] is not { } item || !CanStore(item.Definition.Kind, item.Definition.CatalogKey)
            || !TryCovered(Grid, anchor, item, covered, out var displaced))
        {
            return NpcServiceStatus.Rejected;
        }

        return (displaced is null ? Grid.TryMove(source, anchor) : Grid.TrySwap(source, anchor)) ? Changed() : NpcServiceStatus.Rejected;
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

    private NpcServiceStatus Changed()
    {
        Revision++;
        return NpcServiceStatus.Ok;
    }

    // The one item of `to` that `item` set down at anchor lies over (null on free cells). False when it lies over more,
    // or over another item than the client saw (its view was stale). Anchor -1 (the first free cells) covers nothing.
    private static bool TryCovered(Inventory to, int anchor, ItemInstance item, Guid expected, out ItemInstance displaced)
    {
        displaced = null;
        if (anchor < 0)
        {
            return expected == Guid.Empty;
        }

        var under = to.Covered(anchor, item.Definition, to.Find(item.InstanceId));
        if (under == Inventory.Several)
        {
            return false;
        }

        displaced = under >= 0 ? to[under] : null;
        return (displaced?.InstanceId ?? Guid.Empty) == expected;
    }

    // The item at source of `from` set down at anchor of `to`; the displaced item of `to` goes to the source cell or
    // wherever `from` has room. Both sides are checked before either changes.
    private static bool SetDown(Inventory from, int source, Inventory to, int anchor, ItemInstance displaced)
    {
        var item = from[source];
        if (displaced is null)
        {
            return (anchor >= 0 ? to.TryPlace(item, anchor) : to.TryAdd(item)) && from.TakeAt(source) is not null;
        }

        var under = to.Find(displaced.InstanceId);
        if (!to.CanExchange(under, item.Definition, anchor) || !from.CanExchange(source, displaced.Definition))
        {
            return false;
        }

        return to.TryExchange(under, item, anchor) && from.TryExchange(source, displaced);
    }

    public WarehouseView View()
    {
        var items = new List<OwnedItemView>();
        for (var anchor = 0; anchor < Grid.Capacity; anchor++)
        {
            if (Grid[anchor] is { } item)
            {
                var definition = item.Definition;
                var key = item.Sealed ? PristonTale.Simulation.Items.Usable.Puzzles.LuckyBagIcon : definition.CatalogKey;
                items.Add(new OwnedItemView(item.InstanceId, item.Sealed ? ItemCodes.Parse(key) : definition.Code,
                    item.Sealed ? PristonTale.Simulation.Items.Usable.Puzzles.LuckyBagName : item.DisplayName, definition.Kind, item.Count, anchor, -1,
                    definition.GridWidth, definition.GridHeight, key));
            }
        }

        return new WarehouseView(Revision, Money, Weight, MaxWeight, Columns, Rows, items);
    }
}
