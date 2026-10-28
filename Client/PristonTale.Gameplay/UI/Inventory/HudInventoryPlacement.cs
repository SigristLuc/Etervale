using Microsoft.Xna.Framework;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.UI.Inventory;

public enum HudPlacementKind
{
    // Over the window but nowhere the item can go: a click does nothing.
    None,
    // Above the inventory window: the item leaves the bag (a shop, the warehouse, the ground).
    Outside,
    Fits,
    // Over exactly one other item, which the carried one replaces and which is picked up in turn.
    Exchange,
    // Over a potion of the same kind, which the carried potions join.
    Merge,
    // Over two items, a slot of another kind, or a slot whose requirements the owner does not meet.
    Blocked
}

// Highlight: the rectangle the original colours (the landing cells, slot or belt box, or the covered item). Covered: the
// item a click picks up in exchange, or joins. Belt: the potion belt box it lands in, or -1.
public readonly record struct HudPlacement(HudPlacementKind Kind, Rectangle Highlight, int Anchor = -1, EquipmentSlot? Slot = null,
    OwnedItemView Covered = null, int Belt = -1);

// Where a carried item would go, as the original decides it: the item is drawn centred on the pointer and lands with its
// top-left rounded to the nearest cell; an equipment slot or a potion belt box takes it when the item's frame, less half
// a cell on each side, overlaps it. A belt potion goes back to the bag only on free cells or onto its own kind.
public static class HudInventoryPlacement
{
    private const int HalfCell = HudInventoryLayout.CellSize / 2;
    // The carried item leaves the bag when its frame ends above this line of the 800x600 design (the inventory window
    // and the bottom bar start below it).
    private const int OutsideLine = 420;

    public static Rectangle Frame(OwnedItemView item, Point pointer)
    {
        var width = item.Width * HudInventoryLayout.CellSize;
        var height = item.Height * HudInventoryLayout.CellSize;
        return new Rectangle(pointer.X - width / 2, pointer.Y - height / 2, width, height);
    }

    public static HudPlacement Of(Point origin, IReadOnlyList<OwnedItemView> items, OwnedItemView carried, Point pointer, bool usable)
    {
        var frame = Frame(carried, pointer);
        var inner = new Rectangle(frame.X + HalfCell, frame.Y + HalfCell, frame.Width - 2 * HalfCell, frame.Height - 2 * HalfCell);
        for (var slot = 0; slot < 10; slot++)
        {
            var bounds = HudInventoryLayout.Equipment(origin, slot);
            if (inner.Intersects(bounds))
            {
                return Equipment(items, carried, (EquipmentSlot)slot, bounds, usable);
            }
        }

        for (var box = 0; box < PotionBelt.Boxes; box++)
        {
            var bounds = HudInventoryLayout.Belt(origin, box);
            if (inner.Intersects(bounds))
            {
                return Belt(items, carried, box, bounds);
            }
        }

        var bag = HudInventoryLayout.Bag(origin);
        var column = FloorDivide(frame.X + HalfCell - bag.X, HudInventoryLayout.CellSize);
        var row = FloorDivide(frame.Y + HalfCell - bag.Y, HudInventoryLayout.CellSize);
        if (column >= 0 && row >= 0 && column + carried.Width <= HudInventoryLayout.Columns && row + carried.Height <= HudInventoryLayout.Rows)
        {
            var placement = Bag(origin, items, carried, row * HudInventoryLayout.Columns + column);
            return carried.BeltBox >= 0 && placement.Kind == HudPlacementKind.Exchange ? placement with { Kind = HudPlacementKind.Blocked } : placement;
        }

        return frame.Bottom < origin.Y + OutsideLine ? new HudPlacement(HudPlacementKind.Outside, Rectangle.Empty) : default;
    }

    private static HudPlacement Belt(IReadOnlyList<OwnedItemView> items, OwnedItemView carried, int box, Rectangle bounds)
    {
        if (carried.Kind != ItemKind.Potion)
        {
            return new HudPlacement(HudPlacementKind.Blocked, bounds, Belt: box);
        }

        var held = items.FirstOrDefault(item => item.BeltBox == box && item.InstanceId != carried.InstanceId);
        var kind = held is null ? HudPlacementKind.Fits : held.CatalogKey == carried.CatalogKey ? HudPlacementKind.Merge : HudPlacementKind.Exchange;
        return new HudPlacement(kind, bounds, Covered: held, Belt: box);
    }

    private static HudPlacement Equipment(IReadOnlyList<OwnedItemView> items, OwnedItemView carried, EquipmentSlot slot, Rectangle bounds, bool usable)
    {
        if (!ItemEquipmentMetadata.Fits(carried.Kind, slot) || !usable)
        {
            return new HudPlacement(HudPlacementKind.Blocked, bounds, Slot: slot);
        }

        OwnedItemView covered = null;
        foreach (var item in items)
        {
            if (item.Slot == (int)slot && item.InstanceId != carried.InstanceId)
            {
                covered = item;
            }
        }

        return new HudPlacement(covered is null ? HudPlacementKind.Fits : HudPlacementKind.Exchange, bounds, Slot: slot, Covered: covered);
    }

    private static HudPlacement Bag(Point origin, IReadOnlyList<OwnedItemView> items, OwnedItemView carried, int anchor)
    {
        var target = HudInventoryLayout.Cell(origin, anchor, carried.Width, carried.Height);
        OwnedItemView covered = null;
        foreach (var item in items)
        {
            if (!item.InBag || item.InstanceId == carried.InstanceId
                || !target.Intersects(HudInventoryLayout.Cell(origin, item.Anchor, item.Width, item.Height)))
            {
                continue;
            }

            if (covered is not null)
            {
                return new HudPlacement(HudPlacementKind.Blocked, target, anchor);
            }

            covered = item;
        }

        if (covered is null)
        {
            return new HudPlacement(HudPlacementKind.Fits, target, anchor);
        }

        var cover = HudInventoryLayout.Cell(origin, covered.Anchor, covered.Width, covered.Height);
        var merge = carried.Kind == ItemKind.Potion && covered.CatalogKey == carried.CatalogKey;
        return new HudPlacement(merge ? HudPlacementKind.Merge : HudPlacementKind.Exchange, cover, anchor, Covered: covered);
    }

    private static int FloorDivide(int value, int divisor) => value >= 0 ? value / divisor : (value - divisor + 1) / divisor;
}
