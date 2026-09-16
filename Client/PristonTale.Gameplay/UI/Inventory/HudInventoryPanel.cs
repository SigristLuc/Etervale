using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Inventory;

public sealed class HudInventoryPanel
{
    private static readonly string[] Slots = ["Weapon", "Off hand", "Armor", "Gloves", "Boots", "Bracelet", "Amulet", "Ring L", "Ring R", "Sheltom"];
    private readonly Dictionary<Guid, string> _counts = new();
    private long _revision = -1;
    private long _money = -1;
    private string _gold = "";

    public void Draw(HudCanvas canvas, HudLayout layout, OwnerState state, HudInventoryController controller, HudItemIcons icons, Point pointer)
    {
        if (state is null)
        {
            return;
        }

        if (_revision != state.InventoryRevision)
        {
            _revision = state.InventoryRevision;
            _counts.Clear();
            foreach (var item in state.Items)
            {
                _counts[item.InstanceId] = item.Count > 1 ? item.Count.ToString() : "";
            }
        }

        if (_money != state.Money)
        {
            _money = state.Money;
            _gold = $"Gold  {_money:N0}";
        }

        var panel = layout.Panel;
        var grid = HudInventoryLayout.Bag(layout).Location;
        for (var row = 0; row < 6; row++)
        {
            for (var column = 0; column < 12; column++)
            {
                canvas.Frame(new Rectangle(grid.X + column * layout.CellSize, grid.Y + row * layout.CellSize,
                    layout.CellSize, layout.CellSize), "Slot", 5, new Color(160, 169, 177));
            }
        }

        var occupied = 0u;
        foreach (var item in state.Items)
        {
            if (item.Slot >= 0 && item.Slot < Slots.Length)
            {
                occupied |= 1u << item.Slot;
            }
        }

        for (var slot = 0; slot < Slots.Length; slot++)
        {
            var bounds = controller.EquipmentBounds(slot);
            canvas.Frame(bounds, controller.Dragging != Guid.Empty && bounds.Contains(pointer) ? "Selected" : "Inset", 6);
            if ((occupied & (1u << slot)) == 0)
            {
                canvas.Text(Slots[slot], new Rectangle(bounds.X + 3, bounds.Y + 2, bounds.Width - 6, bounds.Height - 4), HudCanvas.Muted, 0.5f, centered: true);
            }
        }

        OwnedItemView hovered = null;
        OwnedItemView dragged = null;
        foreach (var item in state.Items)
        {
            var bounds = item.Slot >= 0 ? controller.EquipmentBounds(item.Slot)
                : new Rectangle(grid.X + item.Anchor % 12 * layout.CellSize, grid.Y + item.Anchor / 12 * layout.CellSize,
                    item.Width * layout.CellSize, item.Height * layout.CellSize);
            var hover = bounds.Contains(pointer);
            if (hover)
            {
                hovered = item;
            }

            canvas.Frame(bounds, hover ? "Selected" : "Slot", 5);
            DrawItem(canvas, icons, item, bounds, controller.Dragging == item.InstanceId ? Color.White * 0.4f : Color.White);
            if (controller.Dragging == item.InstanceId)
            {
                dragged = item;
            }
        }

        canvas.Text(_gold, new Rectangle(panel.X + 18, panel.Y + 213, 230, 25), scale: 0.75f);
        canvas.Text(controller.Pending ? "Applying..." : controller.Message,
            new Rectangle(panel.X + 254, panel.Y + 216, panel.Width - 274, 20), HudCanvas.Muted, 0.55f);

        if (dragged is not null)
        {
            DrawItem(canvas, icons, dragged, new Rectangle(pointer.X - 16, pointer.Y - 16,
                dragged.Width * layout.CellSize, dragged.Height * layout.CellSize), Color.White * 0.85f);
        }
        else if (hovered is not null)
        {
            canvas.FlushText();
            var tooltip = layout.PlaceOverlay(new Point(pointer.X - 260, pointer.Y - 76), 280, 68);
            canvas.Frame(tooltip, "Panel", 14);
            canvas.Text(hovered.Name, new Rectangle(tooltip.X + 14, tooltip.Y + 10, tooltip.Width - 28, 25), scale: 0.75f);
            var action = "Drag to move  |  Right-click to equip";
            if (hovered.Slot >= 0)
            {
                action = "Right-click to unequip";
            }
            else if (hovered.Kind == PristonTale.Simulation.Items.ItemKind.Potion)
            {
                action = "Right-click to drink one potion";
            }

            canvas.Text(action,
                new Rectangle(tooltip.X + 14, tooltip.Y + 37, tooltip.Width - 28, 18), HudCanvas.Muted, 0.55f);
        }
    }

    private void DrawItem(HudCanvas canvas, HudItemIcons icons, OwnedItemView item, Rectangle bounds, Color tint)
    {
        var texture = icons.Find(item.CatalogKey);
        var inner = new Rectangle(bounds.X + 4, bounds.Y + 4, bounds.Width - 8, bounds.Height - 8);
        if (texture is not null)
        {
            canvas.Icon(texture, inner, tint);
        }
        else
        {
            canvas.Text(item.Name, inner, HudCanvas.Muted, 0.5f, centered: true);
        }

        canvas.Text(_counts.GetValueOrDefault(item.InstanceId, ""), new Rectangle(bounds.Right - 26, bounds.Bottom - 17, 22, 15), Color.White, 0.55f, centered: true);
    }
}
