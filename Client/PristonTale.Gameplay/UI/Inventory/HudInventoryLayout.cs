using Microsoft.Xna.Framework;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Inventory;

// Original inventory slots, bag grid, arrows and text positions in 800x600 design coordinates of the fully open window.
// Origin is the top-left of the bottom-anchored 800x600 frame; panel animation adds its slide offset to Y.
public static class HudInventoryLayout
{
    public const int CellSize = 22;
    public const int Columns = 12;
    public const int Rows = 6;

    public static Point Origin(HudLayout layout, int slideOffset = 0) => new(0, layout.BottomOffset + slideOffset);

    public static Rectangle Bag(Point origin) => new(origin.X + 23, origin.Y + 436, Columns * CellSize, Rows * CellSize);

    public static Rectangle Cell(Point origin, int anchor, int width = 1, int height = 1)
    {
        var bag = Bag(origin);
        return new Rectangle(bag.X + anchor % Columns * CellSize, bag.Y + anchor / Columns * CellSize, width * CellSize, height * CellSize);
    }

    public static int CellAt(Point origin, Point pointer)
    {
        var bag = Bag(origin);
        return bag.Contains(pointer) ? (pointer.Y - bag.Y) / CellSize * Columns + (pointer.X - bag.X) / CellSize : -1;
    }

    public static Rectangle Equipment(Point origin, int slot)
    {
        var bounds = (EquipmentSlot)slot switch
        {
            EquipmentSlot.Weapon => Edges(574 + 1, 433 + 1, 640, 544),
            EquipmentSlot.OffHand => Edges(722 + 1, 433 + 1, 788, 544),
            EquipmentSlot.Armor => Edges(648 + 1, 433 + 1, 714, 544),
            EquipmentSlot.Amulet => Edges(304 + 1, 438 + 1, 326, 460),
            EquipmentSlot.RingRight => Edges(356 + 1, 438 + 1, 378, 460),
            EquipmentSlot.RingLeft => Edges(330 + 1, 438 + 1, 352, 460),
            EquipmentSlot.Sheltom => Edges(382 + 1, 438 + 1, 404, 460),
            EquipmentSlot.Bracelet => Edges(411 + 1, 438 + 1, 455, 482),
            EquipmentSlot.Gloves => Edges(462 + 1, 438 + 1, 506, 482),
            EquipmentSlot.Boots => Edges(513 + 1, 438 + 1, 557, 482),
            _ => Rectangle.Empty
        };
        return bounds == Rectangle.Empty ? bounds : new Rectangle(bounds.X + origin.X, bounds.Y + origin.Y, bounds.Width, bounds.Height);
    }

    // CoverLarge 66x110, CoverMiddle 44x44 and CoverSmall 22x22 are drawn from the slot's top-left behind equipped items.
    public static string Cover(int slot) => (EquipmentSlot)slot switch
    {
        EquipmentSlot.Weapon or EquipmentSlot.OffHand or EquipmentSlot.Armor => Rendering.HudArt.CoverLarge,
        EquipmentSlot.Bracelet or EquipmentSlot.Gloves or EquipmentSlot.Boots => Rendering.HudArt.CoverMiddle,
        _ => Rendering.HudArt.CoverSmall
    };

    // The weapon-set arrows beside the weapon slot: A on the left, B on the right; the worn set's arrow shows its art
    // (drawn 20x20 from the hit box's top-left).
    public const string LeftArrow = "image/Sinimage/Inven/LeftArrow.bmp";
    public const string RightArrow = "image/Sinimage/Inven/RightArrow.bmp";
    public static readonly string[] Art = [LeftArrow, RightArrow];

    public static Rectangle WeaponSet(Point origin, int set) => set == 0
        ? new Rectangle(origin.X + 523, origin.Y + 524, 15, 9)
        : new Rectangle(origin.X + 545, origin.Y + 524, 15, 9);

    // The bag-page arrows at the window's bottom left: I on the left, II on the right, with the same art as the set arrows.
    public static Rectangle BagPage(Point origin, int page) => new(origin.X + (page == 0 ? 26 : 48), origin.Y + 572, 22, 19);

    // The potion belt's three 26x26 slots on the bar (the 1, 2 and 3 keys), always at the bar's place.
    public static Rectangle Belt(Point origin, int slot) => new(origin.X + 495 + 26 * slot, origin.Y + 565, 26, 26);

    public static Rectangle Close(Point origin) => new(origin.X + 266, origin.Y + 572, 22, 19);
    public static Rectangle Money(Point origin) => new(origin.X + 147, origin.Y + 574, 110, 18);
    public static Rectangle Weight(Point origin) => new(origin.X + 326, origin.Y + 471, 80, 15);
    public static Rectangle Window(Point origin) => new(origin.X, origin.Y + 400, HudLayout.DesignWidth, 200);

    private static Rectangle Edges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);
}
