using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Aging;

namespace PristonTale.Gameplay.UI.Npc;

public enum HudWorkActionKind
{
    None,
    Consumed,
    Exit,
    Start
}

// The aging NPC's window in the shop frame. Items dragged from the inventory stay there (dimmed) until the aging starts:
// the item to age in the item box, which shows grey hints of the sheltoms its level asks in the four rows of three, each
// sheltom in a cell that asks it, and an aging stone or copper ore in the stone cell. The start button lights when every
// asked sheltom is in place; the plate shows the price. A right click gives a placed item back.
public sealed class HudAgingWindow
{
    private static readonly Color Placement = Color.FromNonPremultiplied(160, 235, 255, 128);
    private static readonly Color Refusal = Color.FromNonPremultiplied(255, 0, 0, 96);
    private readonly HudSlide _slide = new();
    private readonly Guid[] _sheltoms = new Guid[HudCraftLayout.SheltomSlots];
    private Guid _item;
    private Guid _stone;
    private OwnedItemView _hovered;
    private bool _startHovered;
    private bool _exitHovered;

    public bool IsOpen => _slide.Open;
    public bool Visible => _slide.Visible;
    public bool FullyOpen => _slide.FullyOpen;
    public Point Origin => HudStoreLayout.Origin(_slide.Offset);
    public Guid Item => _item;

    // The materials in the order the request sends them: the sheltoms, then the stone.
    public Guid[] Materials => _sheltoms.Where(static id => id != Guid.Empty).Append(_stone).Where(static id => id != Guid.Empty).ToArray();

    public void Show()
    {
        Clear();
        _slide.Open = true;
    }

    public void Hide()
    {
        _slide.Open = false;
        Clear();
    }

    public void Clear()
    {
        _item = Guid.Empty;
        _stone = Guid.Empty;
        Array.Clear(_sheltoms);
        _hovered = null;
    }

    public void Advance(double seconds) => _slide.Advance(seconds);

    public bool Contains(Point pointer) => _slide.Visible && HudStoreLayout.Window(Origin).Contains(pointer);

    // Items placed in the window are held there and do not move in the inventory.
    public bool Holds(Guid id) => id != Guid.Empty && (id == _item || id == _stone || Array.IndexOf(_sheltoms, id) >= 0);

    // Forgets placed items the inventory no longer has.
    public void Sync(OwnerState state)
    {
        if (state is null)
        {
            return;
        }

        bool Owned(Guid id) => id == Guid.Empty || state.Items.Any(item => item.InstanceId == id && item.Slot < 0);
        if (!Owned(_item))
        {
            Clear();
            return;
        }

        if (!Owned(_stone))
        {
            _stone = Guid.Empty;
        }

        for (var slot = 0; slot < _sheltoms.Length; slot++)
        {
            if (!Owned(_sheltoms[slot]))
            {
                _sheltoms[slot] = Guid.Empty;
            }
        }
    }

    // An inventory item dropped on the window; null when the drop missed the window, else the refusal text ("" when placed).
    public string Place(OwnedItemView item, Point pointer, OwnerState state)
    {
        var origin = Origin;
        if (!FullyOpen || item is null || item.Slot >= 0 || !HudStoreLayout.Window(origin).Contains(pointer))
        {
            return null;
        }

        if (Holds(item.InstanceId))
        {
            return "";
        }

        var key = item.CatalogKey;
        if (AgingRules.StoneOf(key) != AgingStone.None)
        {
            _stone = item.InstanceId;
            return "";
        }

        var ordinal = AgingRules.SheltomOrdinal(key);
        if (ordinal > 0)
        {
            return PlaceSheltom(item.InstanceId, ordinal, HudCraftLayout.SheltomAt(origin, pointer), state) ? "" : "Item can't be aged";
        }

        if (key.StartsWith("OS", StringComparison.OrdinalIgnoreCase))
        {
            return "Item can't be aged";
        }

        if (!Ageable(item))
        {
            return "Item can't be aged";
        }

        Clear();
        _item = item.InstanceId;
        return "";
    }

    public static bool Ageable(OwnedItemView item) =>
        item?.Details is { } details && AgingRules.Ageable(item.CatalogKey, item.Kind, details.UniqueItem, details.MixSheltom, details.AgingGoal > 0,
            details.UpgradeLevel);

    public bool Ready(OwnerState state) => MainItem(state) is { Details: { } details } && Required(details.UpgradeLevel)
        .Select((ordinal, slot) => ordinal == 0 || _sheltoms[slot] != Guid.Empty).All(static filled => filled);

    public long Price(OwnerState state) =>
        MainItem(state) is { Details: { } details } ? AgingRules.Price(details.UpgradeLevel, details.Roll.Price) : 0;

    public HudWorkActionKind Update(MouseState mouse, MouseState previous, Point pointer, OwnerState state, bool blocked)
    {
        var origin = Origin;
        _startHovered = HudCraftLayout.Start(origin).Contains(pointer);
        _exitHovered = HudStoreLayout.Exit(origin).Contains(pointer);
        if (!FullyOpen || blocked)
        {
            return HudWorkActionKind.None;
        }

        var pressed = mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released;
        var rightClicked = mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released;
        if (rightClicked && PlacedAt(pointer) is { } placed)
        {
            Remove(placed);
            return HudWorkActionKind.Consumed;
        }

        if (!pressed)
        {
            return HudWorkActionKind.None;
        }

        if (_exitHovered)
        {
            return HudWorkActionKind.Exit;
        }

        if (_startHovered && Ready(state))
        {
            return HudWorkActionKind.Start;
        }

        return Contains(pointer) ? HudWorkActionKind.Consumed : HudWorkActionKind.None;
    }

    public void Draw(HudPainter painter, HudItemIcons icons, Point pointer, OwnerState state, OwnedItemView incoming, bool interactive)
    {
        var origin = Origin;
        _hovered = null;
        painter.Sprite(HudNpcArt.Window, origin.X, origin.Y + 40);
        var title = HudStoreLayout.Title(origin);
        painter.Sprite(HudCraftArt.AgingTitle, title.X, title.Y);
        var panel = HudCraftLayout.Panel(origin);
        painter.Sprite(HudCraftArt.Panel, panel.X, panel.Y);
        var plate = HudCraftLayout.PricePlate(origin);
        painter.Sprite(HudNpcArt.Gold, plate.X, plate.Y);

        var main = MainItem(state);
        if (main?.Details is { } details)
        {
            var required = Required(details.UpgradeLevel);
            for (var slot = 0; slot < required.Count; slot++)
            {
                var cell = HudCraftLayout.Sheltom(origin, slot);
                painter.Sprite(HudCraftArt.SheltomHint(required[slot]), cell.X, cell.Y);
            }
        }

        if (incoming is not null && interactive)
        {
            DrawPlacement(painter, origin, incoming, pointer, state);
        }

        foreach (var (item, bounds) in Placed(state, origin))
        {
            if (interactive && bounds.Contains(pointer))
            {
                _hovered = item;
            }

            HudShopWindow.DrawIcon(painter, icons, item.CatalogKey, bounds);
        }

        if (Ready(state))
        {
            var start = HudCraftLayout.Start(origin);
            painter.Sprite(HudCraftArt.StartButton, start.X, start.Y);
        }

        if (_exitHovered && interactive)
        {
            var exit = HudStoreLayout.Exit(origin);
            painter.Sprite(HudNpcArt.Exit, exit.X, exit.Y);
        }

        if (main is not null)
        {
            var price = HudCraftLayout.PriceText(origin);
            painter.TextCentered(Price(state).ToString("N0"), price.Left, price.Right, price.Y);
        }
    }

    public void DrawOverlay(HudPainter painter, HudLayout layout, Point pointer, OwnerState state)
    {
        if (_startHovered && Ready(state))
        {
            var info = HudCraftLayout.StartInfo(Origin);
            painter.Sprite(HudCraftArt.AgingStartInfo, info.X, info.Y);
        }
        else if (_hovered is not null)
        {
            HudItemDetailTooltip.Draw(painter, layout, pointer, HudItemDetailTooltip.Lines(_hovered, state, "", "Right-click: take back"),
                HudItemDetailTooltip.Gauge(_hovered, state));
        }
    }

    private void DrawPlacement(HudPainter painter, Point origin, OwnedItemView incoming, Point pointer, OwnerState state)
    {
        var key = incoming.CatalogKey;
        if (AgingRules.StoneOf(key) != AgingStone.None)
        {
            var stone = HudCraftLayout.Stone(origin);
            if (stone.Contains(pointer))
            {
                painter.Fill(stone, Placement);
            }

            return;
        }

        var ordinal = AgingRules.SheltomOrdinal(key);
        if (ordinal > 0)
        {
            var slot = HudCraftLayout.SheltomAt(origin, pointer);
            if (slot >= 0)
            {
                var required = MainItem(state)?.Details is { } details ? Required(details.UpgradeLevel) : [];
                var fits = slot < required.Count && required[slot] == ordinal && _sheltoms[slot] == Guid.Empty;
                painter.Fill(HudCraftLayout.Sheltom(origin, slot), fits ? Placement : Refusal);
            }

            return;
        }

        var box = HudCraftLayout.ItemBox(origin);
        if (box.Contains(pointer))
        {
            painter.Fill(box, Ageable(incoming) ? Placement : Refusal);
        }
    }

    private bool PlaceSheltom(Guid id, int ordinal, int slot, OwnerState state)
    {
        if (MainItem(state)?.Details is not { } details)
        {
            return false;
        }

        var required = Required(details.UpgradeLevel);
        bool Free(int index) => index >= 0 && index < required.Count && required[index] == ordinal && _sheltoms[index] == Guid.Empty;
        if (!Free(slot))
        {
            slot = Enumerable.Range(0, required.Count).FirstOrDefault(Free, -1);
        }

        if (slot < 0)
        {
            return false;
        }

        _sheltoms[slot] = id;
        return true;
    }

    private void Remove(Guid id)
    {
        if (id == _item)
        {
            Clear();
        }
        else if (id == _stone)
        {
            _stone = Guid.Empty;
        }
        else
        {
            var slot = Array.IndexOf(_sheltoms, id);
            if (slot >= 0)
            {
                _sheltoms[slot] = Guid.Empty;
            }
        }
    }

    private Guid? PlacedAt(Point pointer)
    {
        var origin = Origin;
        if (_stone != Guid.Empty && HudCraftLayout.Stone(origin).Contains(pointer))
        {
            return _stone;
        }

        var slot = HudCraftLayout.SheltomAt(origin, pointer);
        if (slot >= 0 && _sheltoms[slot] != Guid.Empty)
        {
            return _sheltoms[slot];
        }

        return _item != Guid.Empty && HudCraftLayout.ItemBox(origin).Contains(pointer) ? _item : null;
    }

    private IEnumerable<(OwnedItemView Item, Rectangle Bounds)> Placed(OwnerState state, Point origin)
    {
        if (state is null)
        {
            yield break;
        }

        foreach (var item in state.Items)
        {
            if (item.InstanceId == _item)
            {
                yield return (item, HudCraftLayout.Centered(HudCraftLayout.ItemBox(origin), item.Width, item.Height));
            }
            else if (item.InstanceId == _stone)
            {
                yield return (item, HudCraftLayout.Stone(origin));
            }
            else if (Array.IndexOf(_sheltoms, item.InstanceId) is >= 0 and var slot)
            {
                yield return (item, HudCraftLayout.Sheltom(origin, slot));
            }
        }
    }

    private OwnedItemView MainItem(OwnerState state) =>
        _item == Guid.Empty ? null : state?.Items.FirstOrDefault(item => item.InstanceId == _item);

    private static IReadOnlyList<int> Required(int level) => level < AgingRules.MaxLevel ? AgingRules.SheltomsFor(level) : [];
}
