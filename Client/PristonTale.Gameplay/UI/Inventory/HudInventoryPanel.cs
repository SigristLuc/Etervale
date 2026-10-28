using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Gameplay.UI.Inventory;

// The original inventory window and its text, with the original equipment covers and selection colors.
public sealed class HudInventoryPanel
{
    private readonly HudItemCounts _counts = new();
    private long _money = -1;
    private string _gold = "";
    private (int Carried, int Max) _weightValue = (-1, -1);
    private string _weight = "";
    private OwnerState _state;
    private OwnedItemView _hovered;
    private OwnedItemView _dragged;
    // What the window colours on the belt boxes, drawn over the bottom bar by DrawBelt: the hovered potion, the landing box.
    private Rectangle _beltSelection;
    private (Rectangle Bounds, Color Color) _beltPlacement;

    // The HUD clock that sweeps the finished puzzle's scan lines.
    public double ClockMs { get; set; }

    // An extra tooltip line for the hovered item, such as what an open shop pays for it.
    public Func<OwnedItemView, string> Note { get; set; }

    public Func<Guid, bool> Withheld { get; set; }

    // stored: the warehouse item on the cursor, whose landing the window colours too.
    public void Draw(HudPainter painter, Point origin, OwnerState state, HudInventoryController controller, HudItemIcons icons,
        Point pointer, bool interactive, OwnedItemView stored = null)
    {
        _hovered = null;
        _dragged = null;
        _beltSelection = Rectangle.Empty;
        _beltPlacement = default;
        painter.Sprite(HudArt.Inventory, origin.X, origin.Y + 400);
        if (state is null)
        {
            return;
        }

        Refresh(state);
        _state = state;
        DrawPuzzleBoard(painter, origin, state);
        foreach (var item in state.Items)
        {
            if (item.Equipped)
            {
                var slot = HudInventoryLayout.Equipment(origin, item.Slot);
                painter.Sprite(HudInventoryLayout.Cover(item.Slot), slot.X, slot.Y);
            }

            if (!Usable(item, state))
            {
                // Items the character cannot use, in the bag or worn (a worn one then adds nothing).
                painter.Fill(HudInventoryLayout.Bounds(origin, item), HudPlacementColors.Blocked);
            }
        }

        var dragged = OwnedItemView.Find(state.Items, controller.Dragging);
        var hovered = interactive && stored is null ? controller.Hovered : null;
        if (hovered?.BeltBox >= 0)
        {
            _beltSelection = HudInventoryLayout.Belt(origin, hovered.BeltBox);
        }
        else if (hovered is not null)
        {
            painter.Fill(HudInventoryLayout.Bounds(origin, hovered), HudPlacementColors.Selection);
        }

        if ((dragged ?? stored) is { } carried && interactive)
        {
            var placement = dragged is null
                ? HudInventoryPlacement.OfStored(origin, state.Items, carried, pointer)
                : HudInventoryPlacement.Of(origin, state.Items, carried, pointer, Usable(carried, state));
            if (placement.Belt >= 0)
            {
                _beltPlacement = (placement.Highlight, HudPlacementColors.Of(placement.Kind));
            }
            else
            {
                HudPlacementColors.Draw(painter, placement);
            }
        }

        foreach (var item in state.Items)
        {
            if (item.BeltBox >= 0)
            {
                continue;
            }

            var tint = controller.Dragging == item.InstanceId || Withheld?.Invoke(item.InstanceId) == true ? HudPlacementColors.Carried : Color.White;
            _counts.Draw(painter, icons, item, HudInventoryLayout.Bounds(origin, item), tint);
        }

        DrawSides(painter, origin, state, pointer, interactive);
        var money = HudInventoryLayout.Money(origin);
        painter.TextCentered(_gold, money.Left, money.Right, money.Y);
        var weight = HudInventoryLayout.Weight(origin);
        painter.TextCentered(_weight, weight.Left, weight.Right, weight.Y);
        _hovered = hovered;
        _dragged = dragged;
    }

    // While a puzzle piece or lucky bag is carried, the board backdrop covers the last four columns' top four rows; a
    // finished puzzle glows yellow under two orange lines that sweep the board one pixel every three 70 Hz frames.
    private void DrawPuzzleBoard(HudPainter painter, Point origin, OwnerState state)
    {
        var carried = state.Items.Where(static item => item.Slot < 0).ToList();
        if (!carried.Any(static item => Puzzles.IsPiece(item.CatalogKey) || item.CatalogKey.Equals(Puzzles.LuckyBagIcon, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var board = HudInventoryLayout.Cell(origin, Puzzles.BoardColumn, Puzzles.BoardSize, Puzzles.BoardSize);
        painter.Sprite(HudArt.PuzzleBoard, board.X, board.Y);
        if (!Puzzles.Complete(carried.Select(static item => (item.CatalogKey, item.Anchor)), HudInventoryLayout.Columns))
        {
            return;
        }

        painter.Fill(board, Color.FromNonPremultiplied(255, 255, 0, 30));
        var travel = board.Height - 6;
        var step = (int)(ClockMs / (3000d / 70d)) % (travel * 2);
        var offset = step < travel ? step : travel * 2 - step;
        var line = Color.FromNonPremultiplied(255, 123, 0, 180);
        painter.Fill(new Rectangle(board.X, board.Y + offset, board.Width, 2), line);
        painter.Fill(new Rectangle(board.X, board.Bottom - 2 - offset, board.Width, 2), line);
    }

    // Drawn after the bottom bar so the held item and the item box stay on top, as the original draws items after the interface.
    public void DrawOverlay(HudPainter painter, HudLayout layout, HudItemIcons icons, Point pointer)
    {
        if (_dragged is not null)
        {
            _counts.Draw(painter, icons, _dragged, HudGridPlacement.Frame(_dragged, pointer), Color.White);
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

    // The belt's potions over the bottom bar (drawn after it, the window open or not), with the window's colours on its
    // boxes; origin is the window's origin when fully open (HudInventoryLayout.Origin).
    public void DrawBelt(HudPainter painter, Point origin, OwnerState state, HudInventoryController controller, HudItemIcons icons)
    {
        if (state is null)
        {
            return;
        }

        Refresh(state);
        if (_beltSelection != Rectangle.Empty)
        {
            painter.Fill(_beltSelection, HudPlacementColors.Selection);
        }

        if (_beltPlacement.Color != Color.Transparent)
        {
            painter.Fill(_beltPlacement.Bounds, _beltPlacement.Color);
        }

        foreach (var item in state.Items)
        {
            if (item.BeltBox >= 0)
            {
                _counts.Draw(painter, icons, item, HudInventoryLayout.Bounds(origin, item), controller.Dragging == item.InstanceId ? HudPlacementColors.Carried : Color.White);
            }
        }

        // Set again by the next Draw of the window; a closed window colours nothing.
        _beltSelection = Rectangle.Empty;
        _beltPlacement = default;
    }

    // The original lights the shown page's and the held pair's arrow, and the one under the pointer.
    private static void DrawSides(HudPainter painter, Point origin, OwnerState state, Point pointer, bool interactive)
    {
        for (var side = 0; side < PlayerCharacter.Sides; side++)
        {
            var art = side == 0 ? HudArt.SideLeft : HudArt.SideRight;
            var page = HudInventoryLayout.PageButton(origin, side);
            if (side == state.BagPage || interactive && page.Contains(pointer))
            {
                painter.Sprite(art, page.X, page.Y);
            }

            var pair = HudInventoryLayout.PairButton(origin, side);
            if (side == state.WeaponSet || interactive && pair.Contains(pointer))
            {
                painter.Sprite(art, pair.X, pair.Y);
            }
        }
    }

    private void DrawTooltip(HudPainter painter, HudLayout layout, OwnedItemView item, Point pointer)
    {
        var usable = item.InBag && UsableItemCatalog.IsUsable(item.CatalogKey);
        var action = item.Equipped ? "Right-click: unequip"
            : item.Kind == ItemKind.Potion ? "Right-click: drink one"
            : usable ? "Right-click: use"
            : "Click: carry  Shift-click: split  Right-click: equip";
        var count = _counts.Label(item);
        var named = count.Length > 0 ? item with { Name = $"{item.Name} ({count})" } : item;
        var note = Note?.Invoke(item) ?? "";
        if (usable && note.Length == 0)
        {
            HudUsableTooltip.Draw(painter, layout, pointer, named.Name, HudUsableTooltip.Lines(item.CatalogKey), action);
            return;
        }

        HudItemDetailTooltip.Draw(painter, layout, pointer, HudItemDetailTooltip.Lines(named, _state, note, action),
            HudItemDetailTooltip.Gauge(item, _state));
    }

    private void Refresh(OwnerState state)
    {
        _counts.Refresh(state.InventoryRevision, state.Items);
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
