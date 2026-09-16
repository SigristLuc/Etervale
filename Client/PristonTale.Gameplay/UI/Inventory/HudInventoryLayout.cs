using Microsoft.Xna.Framework;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Inventory;

public static class HudInventoryLayout
{
    public static Rectangle Bag(HudLayout layout) => new(layout.Panel.X + 16, layout.Panel.Y + 44, 12 * layout.CellSize, 6 * layout.CellSize);

    public static Rectangle Equipment(HudLayout layout, int slot)
    {
        var bag = Bag(layout);
        var x = bag.Right + 24;
        var y = bag.Y;
        return (EquipmentSlot)slot switch
        {
            EquipmentSlot.Weapon => new Rectangle(x + 150, y + 48, 58, 120),
            EquipmentSlot.Armor => new Rectangle(x + 214, y + 48, 72, 120),
            EquipmentSlot.OffHand => new Rectangle(x + 292, y + 48, 58, 120),
            EquipmentSlot.Gloves => new Rectangle(x + 150, y, 58, 42),
            EquipmentSlot.Boots => new Rectangle(x + 214, y, 72, 42),
            EquipmentSlot.Bracelet => new Rectangle(x + 292, y, 58, 42),
            EquipmentSlot.Amulet => new Rectangle(x + 48, y + 10, 42, 42),
            EquipmentSlot.RingLeft => new Rectangle(x, y + 64, 42, 42),
            EquipmentSlot.RingRight => new Rectangle(x + 96, y + 64, 42, 42),
            EquipmentSlot.Sheltom => new Rectangle(x + 48, y + 118, 42, 42),
            _ => Rectangle.Empty
        };
    }
}
