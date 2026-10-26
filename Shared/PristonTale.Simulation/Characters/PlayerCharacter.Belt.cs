using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Characters;

// The potion belt: three slots beside the bar, drunk with the 1, 2 and 3 keys. A slot holds one stack of one potion up
// to the belt capacity (PotionSpace); the belt is carried and weighed like the bag.
public sealed partial class PlayerCharacter
{
    public const int BeltSlots = 3;

    public Inventory Belt { get; private set; } = new(BeltSlots, 1);

    public static bool BeltHolds(ItemInstance item) => item?.Definition is { Kind: ItemKind.Potion, Potion.IsValid: true };

    // A stack in the bag or on the belt.
    public ItemInstance FindCarried(Guid instanceId)
    {
        var anchor = Inventory.Find(instanceId);
        if (anchor >= 0)
        {
            return Inventory[anchor];
        }

        var slot = Belt.Find(instanceId);
        return slot >= 0 ? Belt[slot] : null;
    }

    public bool TryConsumePotion(Guid instanceId)
    {
        var (grid, index) = Locate(instanceId);
        return index >= 0 && BeltHolds(grid[index]) && grid.ConsumeAt(index);
    }

    public bool TryDropCarried(Guid instanceId)
    {
        var (grid, index) = Locate(instanceId);
        return index >= 0 && ItemDropRules.CanDrop(this, grid[index]) && grid.TakeAt(index) is not null;
    }

    // A potion stack put on a belt slot: an empty slot takes up to the belt capacity and a slot of the same potion is topped
    // up to it; a slot of another potion trades places with the stack, its potions going to the bag when part of the moved
    // stack stays behind. What does not fit stays where it was.
    public bool TryMoveToBelt(Guid instanceId, int slot)
    {
        var (grid, index) = Locate(instanceId);
        if (slot < 0 || slot >= BeltSlots || index < 0 || !BeltHolds(grid[index]) || grid == Belt && index == slot)
        {
            return false;
        }

        var source = grid[index];
        var space = Derived.PotionSpace;
        var target = Belt[slot];
        if (target is null || Inventory.CanStack(target, source))
        {
            var amount = Math.Min(source.Count, space - (target?.Count ?? 0));
            if (amount <= 0)
            {
                return false;
            }

            var moved = amount == source.Count ? source.Copy(amount, preserveId: true) : source.Copy(amount);
            grid.TryChangeCount(index, -amount);
            return target is null ? Belt.TryPlace(moved, slot) : Belt.TryChangeCount(slot, amount);
        }

        Belt.TakeAt(slot);
        if (source.Count <= space)
        {
            grid.TakeAt(index);
            return grid.TryPlace(target, index) && Belt.TryPlace(source, slot);
        }

        if (!Inventory.TryAdd(target))
        {
            Belt.TryPlace(target, slot);
            return false;
        }

        grid.TryChangeCount(index, -space);
        return Belt.TryPlace(source.Copy(space), slot);
    }

    // A belt stack put in the bag: on a stack of the same potion it merges up to the stack limit, on a free place it goes
    // there, and with no place named (anchor -1) it joins the bag's stacks and first free places.
    public bool TryMoveFromBelt(Guid instanceId, int anchor)
    {
        var slot = Belt.Find(instanceId);
        if (slot < 0 || anchor >= Inventory.Capacity)
        {
            return false;
        }

        var item = Belt[slot];
        if (anchor >= 0 && Inventory[anchor] is { } stack)
        {
            var amount = Math.Min(item.Count, stack.Definition.MaxStack - stack.Count);
            return Inventory.CanStack(stack, item) && amount > 0 && Inventory.TryChangeCount(anchor, amount) && Belt.TryChangeCount(slot, -amount);
        }

        Belt.TakeAt(slot);
        if (anchor >= 0 ? Inventory.TryPlace(item, anchor) : Inventory.TryAdd(item))
        {
            return true;
        }

        Belt.TryPlace(item, slot);
        return false;
    }

    // Bought and picked-up potions go to the belt first: slots of the same potion are topped up to the belt capacity, then
    // the first empty slot takes a new stack, and the rest joins the bag. Nothing changes when the bag has no room for the rest.
    // Other items go to the bag.
    public bool TryCarry(ItemInstance stack)
    {
        if (!BeltHolds(stack))
        {
            return Inventory.TryAdd(stack);
        }

        var space = Derived.PotionSpace;
        Span<int> planned = stackalloc int[BeltSlots];
        var remaining = stack.Count;
        for (var slot = 0; slot < BeltSlots && remaining > 0; slot++)
        {
            if (Belt[slot] is { } held && Inventory.CanStack(held, stack))
            {
                planned[slot] = Math.Min(remaining, Math.Max(0, space - held.Count));
                remaining -= planned[slot];
            }
        }

        for (var slot = 0; slot < BeltSlots && remaining > 0; slot++)
        {
            if (Belt[slot] is null)
            {
                planned[slot] = Math.Min(remaining, space);
                remaining -= planned[slot];
                break;
            }
        }

        if (remaining > 0 && !Inventory.TryAdd(stack.Copy(remaining, preserveId: remaining == stack.Count)))
        {
            return false;
        }

        for (var slot = 0; slot < BeltSlots; slot++)
        {
            if (planned[slot] > 0 && Belt[slot] is null)
            {
                Belt.TryPlace(stack.Copy(planned[slot]), slot);
            }
            else if (planned[slot] > 0)
            {
                Belt.TryChangeCount(slot, planned[slot]);
            }
        }

        return true;
    }

    // When the belt capacity falls (the item that raised it is taken off), what a slot holds beyond it goes to the bag
    // while the bag has room; otherwise the slot keeps it and takes no more until it is drunk below the capacity.
    public void SettleBelt()
    {
        var space = Derived.PotionSpace;
        for (var slot = 0; slot < BeltSlots; slot++)
        {
            if (Belt[slot] is { } held && held.Count > space && Inventory.TryAdd(held.Copy(held.Count - space)))
            {
                Belt.TryChangeCount(slot, space - held.Count);
            }
        }
    }

    public void RestoreBelt(IEnumerable<(int Slot, ItemInstance Item)> stacks)
    {
        Belt.Changed -= OnInventoryChanged;
        Belt = new Inventory(BeltSlots, 1);
        foreach (var (slot, item) in stacks)
        {
            if (!BeltHolds(item) || !Belt.TryPlace(item, slot))
            {
                throw new ArgumentException("Invalid belt stack.", nameof(stacks));
            }
        }

        Belt.Changed += OnInventoryChanged;
        OnInventoryChanged();
    }

    public IEnumerable<(int Slot, ItemInstance Item)> BeltItems()
    {
        for (var slot = 0; slot < BeltSlots; slot++)
        {
            if (Belt[slot] is { } item)
            {
                yield return (slot, item);
            }
        }
    }

    private (Inventory Grid, int Index) Locate(Guid instanceId)
    {
        var anchor = Inventory.Find(instanceId);
        return anchor >= 0 ? (Inventory, anchor) : (Belt, Belt.Find(instanceId));
    }
}
