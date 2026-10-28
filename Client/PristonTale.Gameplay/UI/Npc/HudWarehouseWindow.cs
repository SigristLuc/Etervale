using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Npc;

public enum HudWarehouseActionKind
{
    None,
    Consumed,
    Exit,
    Move,
    Withdraw
}

public readonly record struct HudWarehouseAction(HudWarehouseActionKind Kind, Guid ItemId = default, int Anchor = -1);

// The account warehouse in the original window: stored gold in the upper bar, weight in the lower one and the 9x9 grid.
// Items are dragged between the grid and the inventory; a right click takes an item to the first free inventory cell.
public sealed class HudWarehouseWindow : IHudServiceWindow
{
    private static readonly Color Placement = Color.FromNonPremultiplied(160, 235, 255, 128);
    private static readonly Color Selection = Color.FromNonPremultiplied(255, 200, 0, 128);
    private readonly HudSlide _slide = new();
    private OwnedItemView _hovered;
    private OwnedItemView _dragged;
    private Guid _dragging;

    public WarehouseView View { get; private set; }
    public bool IsOpen => _slide.Open;
    public bool Visible => _slide.Visible;
    public bool FullyOpen => _slide.FullyOpen;
    public Point Origin => HudStoreLayout.Origin(_slide.Offset);
    public Guid Dragging => _dragging;

    public void Show(WarehouseView view)
    {
        View = view;
        _dragging = Guid.Empty;
        _slide.Open = true;
    }

    public void Refresh(WarehouseView view) => View = view ?? View;

    public void Hide()
    {
        _slide.Open = false;
        _dragging = Guid.Empty;
        _hovered = null;
    }

    public void CancelDrag() => _dragging = Guid.Empty;

    public void Advance(double seconds) => _slide.Advance(seconds);

    public bool BlocksWorld => true;

    void IHudServiceWindow.Draw(in HudServiceFrame frame) => Draw(frame.Painter, frame.Icons, frame.Pointer, frame.Incoming, FullyOpen && !frame.SettingsOpen);

    void IHudServiceWindow.DrawOverlay(in HudServiceFrame frame) => DrawOverlay(frame.Painter, frame.Layout, frame.Icons, frame.Pointer);

    public bool Contains(Point pointer) => _slide.Visible && HudStoreLayout.Window(Origin).Contains(pointer);

    public HudWarehouseAction Update(MouseState mouse, MouseState previous, Point pointer, Func<Point, int> inventoryCellAt, bool blocked)
    {
        if (!FullyOpen || View is null || blocked)
        {
            _dragging = Guid.Empty;
            return default;
        }

        var pressed = mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released;
        var released = mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed;
        var rightClicked = mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released;
        var item = ItemAt(pointer);
        if (rightClicked && item is not null)
        {
            return new HudWarehouseAction(HudWarehouseActionKind.Withdraw, item.InstanceId);
        }

        if (pressed && HudStoreLayout.Exit(Origin).Contains(pointer))
        {
            return new HudWarehouseAction(HudWarehouseActionKind.Exit);
        }

        if (pressed && item is not null)
        {
            _dragging = item.InstanceId;
            return new HudWarehouseAction(HudWarehouseActionKind.Consumed);
        }

        if (!released || _dragging == Guid.Empty)
        {
            return pressed && Contains(pointer) ? new HudWarehouseAction(HudWarehouseActionKind.Consumed) : default;
        }

        var identity = _dragging;
        _dragging = Guid.Empty;
        var cell = HudStoreLayout.CellAt(Origin, pointer);
        if (cell >= 0)
        {
            var source = View.Items.FirstOrDefault(candidate => candidate.InstanceId == identity);
            return source is not null && source.Anchor != cell
                ? new HudWarehouseAction(HudWarehouseActionKind.Move, identity, cell)
                : new HudWarehouseAction(HudWarehouseActionKind.Consumed);
        }

        var inventoryCell = inventoryCellAt(pointer);
        return inventoryCell >= 0 ? new HudWarehouseAction(HudWarehouseActionKind.Withdraw, identity, inventoryCell) : default;
    }

    public void Draw(HudPainter painter, HudItemIcons icons, Point pointer, OwnedItemView incoming, bool interactive)
    {
        var origin = Origin;
        _hovered = null;
        _dragged = null;
        painter.Sprite(HudNpcArt.Window, origin.X, origin.Y + 40);
        var title = HudStoreLayout.WarehouseTitle(origin);
        painter.Sprite(HudNpcArt.WarehouseTitle, title.X, title.Y);
        if (View is null)
        {
            return;
        }

        foreach (var item in View.Items)
        {
            var bounds = HudStoreLayout.Cell(origin, item.Anchor, item.Width, item.Height);
            if (item.InstanceId == _dragging)
            {
                _dragged = item;
            }
            else if (interactive && bounds.Contains(pointer))
            {
                _hovered = item;
                painter.Fill(bounds, Selection);
            }
        }

        var moving = _dragged ?? incoming;
        var cell = HudStoreLayout.CellAt(origin, pointer);
        if (moving is not null && cell >= 0)
        {
            var target = HudStoreLayout.Cell(origin, cell, moving.Width, moving.Height);
            painter.Fill(Rectangle.Intersect(target, HudStoreLayout.Grid(origin)), Placement);
        }

        foreach (var item in View.Items)
        {
            var tint = item.InstanceId == _dragging ? Color.White * 0.4f : Color.White;
            var bounds = HudStoreLayout.Cell(origin, item.Anchor, item.Width, item.Height);
            HudShopWindow.DrawIcon(painter, icons, item.CatalogKey, bounds, tint);
            if (item.Count > 1)
            {
                var count = item.Count.ToString();
                painter.Text(count, bounds.Right - painter.Measure(count) - 1, bounds.Bottom - 13);
            }
        }

        var plate = HudStoreLayout.GoldPlate(origin);
        painter.Sprite(HudNpcArt.Gold, plate.X, plate.Y);
        var weight = HudStoreLayout.WeightIcon(origin);
        painter.Sprite(HudNpcArt.Weight, weight.X, weight.Y);
        var upper = HudStoreLayout.UpperBar(origin);
        painter.TextCentered(View.Money.ToString("N0"), upper.Left, upper.Right, upper.Y + 3);
        var lower = HudStoreLayout.LowerBar(origin);
        painter.TextCentered($"{View.Weight}/{View.MaxWeight}", lower.Left + 18, lower.Right, lower.Y + 3);
    }

    public void DrawOverlay(HudPainter painter, HudLayout layout, HudItemIcons icons, Point pointer)
    {
        if (_dragged is not null)
        {
            var size = new Point(_dragged.Width * HudStoreLayout.CellSize, _dragged.Height * HudStoreLayout.CellSize);
            HudShopWindow.DrawIcon(painter, icons, _dragged.CatalogKey, new Rectangle(pointer.X - size.X / 2, pointer.Y - size.Y / 2, size.X, size.Y));
        }
        else if (_hovered is not null)
        {
            var name = _hovered.Count > 1 ? $"{_hovered.Name} ({_hovered.Count})" : _hovered.Name;
            HudItemTooltip.Draw(painter, layout, pointer, name, "Drag to the inventory to take it", "Right-click: take");
        }
    }

    private OwnedItemView ItemAt(Point pointer)
    {
        var origin = Origin;
        foreach (var item in View.Items)
        {
            if (HudStoreLayout.Cell(origin, item.Anchor, item.Width, item.Height).Contains(pointer))
            {
                return item;
            }
        }

        return null;
    }
}
