using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.UI.Inventory;

// The original inventory window and its text, with the original equipment covers and selection colors.
public sealed class HudInventoryPanel
{
    private static readonly Color Placement = Color.FromNonPremultiplied(160, 235, 255, 128);
    private static readonly Color Selection = Color.FromNonPremultiplied(255, 200, 0, 128);
    // Items the character cannot use, in the bag or worn (a worn one then adds nothing).
    private static readonly Color NotUsable = Color.FromNonPremultiplied(255, 0, 0, 64);
    private readonly Dictionary<Guid, string> _counts = new();
    private long _revision = -1;
    private long _money = -1;
    private string _gold = "";
    private (int Carried, int Max) _weightValue = (-1, -1);
    private string _weight = "";
    private OwnerState _state;
    private OwnedItemView _hovered;
    private OwnedItemView _dragged;

    // An extra tooltip line for the hovered item, such as what an open shop pays for it.
    public Func<OwnedItemView, string> Note { get; set; }

    public void Draw(HudPainter painter, Point origin, OwnerState state, HudInventoryController controller, HudItemIcons icons,
        Point pointer, bool interactive)
    {
        _hovered = null;
        _dragged = null;
        painter.Sprite(HudArt.Inventory, origin.X, origin.Y + 400);
        if (state is null)
        {
            return;
        }

        Refresh(state);
        _state = state;
        foreach (var item in state.Items)
        {
            if (item.Slot >= 0)
            {
                var slot = HudInventoryLayout.Equipment(origin, item.Slot);
                painter.Sprite(HudInventoryLayout.Cover(item.Slot), slot.X, slot.Y);
            }

            if (!Usable(item, state))
            {
                painter.Fill(Bounds(origin, item), NotUsable);
            }
        }

        OwnedItemView hovered = null;
        OwnedItemView dragged = null;
        foreach (var item in state.Items)
        {
            var bounds = Bounds(origin, item);
            if (controller.Dragging == item.InstanceId)
            {
                dragged = item;
            }
            else if (interactive && bounds.Contains(pointer))
            {
                hovered = item;
                painter.Fill(bounds, Selection);
            }
        }

        if (dragged is not null && interactive)
        {
            DrawPlacement(painter, origin, dragged, pointer, Usable(dragged, state));
        }

        foreach (var item in state.Items)
        {
            var tint = controller.Dragging == item.InstanceId ? Color.White * 0.4f : Color.White;
            DrawItem(painter, icons, item, Bounds(origin, item), tint);
        }

        var money = HudInventoryLayout.Money(origin);
        painter.TextCentered(_gold, money.Left, money.Right, money.Y);
        var weight = HudInventoryLayout.Weight(origin);
        painter.TextCentered(_weight, weight.Left, weight.Right, weight.Y);
        _hovered = hovered;
        _dragged = dragged;
    }

    // Drawn after the bottom bar so the held item and the item box stay on top, as the original draws items after the interface.
    public void DrawOverlay(HudPainter painter, HudLayout layout, HudItemIcons icons, Point pointer)
    {
        if (_dragged is not null)
        {
            var size = new Point(_dragged.Width * HudInventoryLayout.CellSize, _dragged.Height * HudInventoryLayout.CellSize);
            DrawItem(painter, icons, _dragged, new Rectangle(pointer.X - size.X / 2, pointer.Y - size.Y / 2, size.X, size.Y), Color.White);
        }
        else if (_hovered is not null)
        {
            DrawTooltip(painter, layout, _hovered, pointer);
        }
    }

    // Requirements are checked against the owner's current level and attributes, as the original re-checks them on every
    // stat change; items without details count as usable.
    public static bool Usable(OwnedItemView item, OwnerState state) =>
        item.Details is null || state is null || item.Details.Usable(state.Level,
            new CharacterAttributes(state.Strength, state.Spirit, state.Talent, state.Agility, state.Health));

    private static void DrawPlacement(HudPainter painter, Point origin, OwnedItemView dragged, Point pointer, bool usable)
    {
        var cell = HudInventoryLayout.CellAt(origin, pointer);
        if (cell >= 0)
        {
            var target = HudInventoryLayout.Cell(origin, cell, dragged.Width, dragged.Height);
            painter.Fill(Rectangle.Intersect(target, HudInventoryLayout.Bag(origin)), Placement);
            return;
        }

        for (var slot = 0; slot < 10; slot++)
        {
            var bounds = HudInventoryLayout.Equipment(origin, slot);
            if (bounds.Contains(pointer))
            {
                painter.Fill(bounds, usable ? Placement : NotUsable);
                return;
            }
        }
    }

    private void DrawTooltip(HudPainter painter, HudLayout layout, OwnedItemView item, Point pointer)
    {
        var action = item.Slot >= 0 ? "Right-click: unequip"
            : item.Kind == ItemKind.Potion ? "Right-click: drink one"
            : "Drag: move  Shift-drag: split  Right-click: equip";
        var count = _counts.GetValueOrDefault(item.InstanceId, "");
        var named = count.Length > 0 ? item with { Name = $"{item.Name} ({count})" } : item;
        var note = Note?.Invoke(item) ?? "";
        HudItemDetailTooltip.Draw(painter, layout, pointer, HudItemDetailTooltip.Lines(named, _state, note, action));
    }

    public static Rectangle Bounds(Point origin, OwnedItemView item) =>
        item.Slot >= 0 ? HudInventoryLayout.Equipment(origin, item.Slot) : HudInventoryLayout.Cell(origin, item.Anchor, item.Width, item.Height);

    private void DrawItem(HudPainter painter, HudItemIcons icons, OwnedItemView item, Rectangle bounds, Color tint)
    {
        var texture = icons.Find(item.CatalogKey);
        if (texture is not null)
        {
            // Item bitmaps are authored at their cell size; draw unscaled and centered as the original inventory does.
            var x = bounds.X + (bounds.Width - texture.Width) / 2;
            var y = bounds.Y + (bounds.Height - texture.Height) / 2;
            painter.Image(texture, new Rectangle(x, y, texture.Width, texture.Height), tint);
        }
        else
        {
            painter.Fill(new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4), Color.FromNonPremultiplied(60, 60, 60, 160));
        }

        var count = _counts.GetValueOrDefault(item.InstanceId, "");
        if (count.Length > 0 && item.Slot < 0)
        {
            painter.Text(count, bounds.Right - painter.Measure(count) - 1, bounds.Bottom - 13);
        }
    }

    private void Refresh(OwnerState state)
    {
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
            _gold = _money.ToString("N0");
        }

        if (_weightValue != (state.CarriedWeight, state.MaxWeight))
        {
            _weightValue = (state.CarriedWeight, state.MaxWeight);
            _weight = $"{state.CarriedWeight}/{state.MaxWeight}";
        }
    }
}
