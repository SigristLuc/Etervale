using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI.Npc;

// The original work windows of the aging and mixing NPCs in 800x600 design coordinates, inside the shop frame: the work
// panel, the item box of 3x4 cells, the four rows of three sheltom cells, the stone cell, the start button and the price
// plate.
public static class HudCraftLayout
{
    public const int CellSize = 22;
    public const int SheltomRows = 4;
    public const int SheltomColumns = 3;
    public const int SheltomSlots = SheltomRows * SheltomColumns;

    public static Point Panel(Point origin) => new(origin.X + 18, origin.Y + 85);
    public static Rectangle ItemBox(Point origin) => new(origin.X + 58, origin.Y + 143, 3 * CellSize, 4 * CellSize);

    public static Rectangle Sheltom(Point origin, int slot) =>
        new(origin.X + 218 + slot % SheltomColumns * CellSize, origin.Y + 143 + slot / SheltomColumns * CellSize, CellSize, CellSize);

    public static Rectangle SheltomGrid(Point origin) => new(origin.X + 218, origin.Y + 143, SheltomColumns * CellSize, SheltomRows * CellSize);

    public static int SheltomAt(Point origin, Point pointer)
    {
        var grid = SheltomGrid(origin);
        return grid.Contains(pointer) ? (pointer.Y - grid.Y) / CellSize * SheltomColumns + (pointer.X - grid.X) / CellSize : -1;
    }

    public static Rectangle Stone(Point origin) => new(origin.X + 159, origin.Y + 218, CellSize, CellSize);
    public static Rectangle Start(Point origin) => new(origin.X + 158, origin.Y + 172, 26, 26);
    public static Point StartInfo(Point origin) => new(origin.X + 131, origin.Y + 145);
    public static Point PricePlate(Point origin) => new(origin.X + 200, origin.Y + 264);
    public static Rectangle PriceText(Point origin) => new(origin.X + 200, origin.Y + 273, 75, 14);

    // An item of width x height cells centred in a box.
    public static Rectangle Centered(Rectangle box, int width, int height)
    {
        var size = new Point(width * CellSize, height * CellSize);
        return new Rectangle(box.X + (box.Width - size.X) / 2, box.Y + (box.Height - size.Y) / 2, size.X, size.Y);
    }
}
