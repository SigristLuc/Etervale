using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Aging;
using PristonTale.Simulation.Items.Mixing;

namespace PristonTale.Gameplay.UI.Npc;

public sealed class HudMixWindow : IHudServiceWindow
{
    private static readonly Color Placement = Color.FromNonPremultiplied(160, 235, 255, 128);
    private static readonly Color Refusal = Color.FromNonPremultiplied(255, 0, 0, 96);
    private readonly HudSlide _slide = new();
    private readonly Guid[] _materials = new Guid[HudCraftLayout.SheltomSlots];
    private Guid _item;
    private Guid _stone;
    private int _pricePercent = 100;
    private OwnedItemView _hovered;
    private bool _startHovered;
    private bool _exitHovered;

    public bool IsOpen => _slide.Open;
    public bool Visible => _slide.Visible;
    public bool FullyOpen => _slide.FullyOpen;
    public bool Alchemy { get; private set; }
    public Point Origin => HudStoreLayout.Origin(_slide.Offset);
    public Guid Item => _item;

    public Guid[] Materials => _materials.Where(static id => id != Guid.Empty).Append(_stone).Where(static id => id != Guid.Empty).ToArray();

    public void Show(bool alchemy, int pricePercent)
    {
        Clear();
        Alchemy = alchemy;
        _pricePercent = Math.Max(0, pricePercent);
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
        Array.Clear(_materials);
        _hovered = null;
    }

    public void Advance(double seconds) => _slide.Advance(seconds);

    public bool BlocksWorld => true;

    void IHudServiceWindow.Draw(in HudServiceFrame frame) => Draw(frame.Painter, frame.Icons, frame.Pointer, frame.State, frame.Incoming, FullyOpen && !frame.SettingsOpen);

    void IHudServiceWindow.DrawOverlay(in HudServiceFrame frame) => DrawOverlay(frame.Painter, frame.Layout, frame.Pointer, frame.State);

    public bool Contains(Point pointer) => _slide.Visible && HudStoreLayout.Window(Origin).Contains(pointer);

    public bool Holds(Guid id) => id != Guid.Empty && (id == _item || id == _stone || Array.IndexOf(_materials, id) >= 0);

    public void Sync(OwnerState state)
    {
        if (state is null)
        {
            return;
        }

        bool Owned(Guid id) => id == Guid.Empty || state.Items.Any(item => item.InstanceId == id && item.Slot < 0);
        if (!Owned(_item))
        {
            _item = Guid.Empty;
        }

        if (!Owned(_stone))
        {
            _stone = Guid.Empty;
        }

        for (var slot = 0; slot < _materials.Length; slot++)
        {
            if (!Owned(_materials[slot]))
            {
                _materials[slot] = Guid.Empty;
            }
        }
    }

    // Null when the drop missed the window, else the refusal text ("" when placed).
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
        if (Accepts(key))
        {
            var slot = HudCraftLayout.SheltomAt(origin, pointer);
            if (slot < 0 || _materials[slot] != Guid.Empty)
            {
                slot = Array.IndexOf(_materials, Guid.Empty);
            }

            if (slot < 0)
            {
                return "Item can't be mixed";
            }

            _materials[slot] = item.InstanceId;
            return "";
        }

        if (Alchemy)
        {
            return "Item can't be mixed";
        }

        if (key.Equals(MixingWork.RespecStone, StringComparison.OrdinalIgnoreCase))
        {
            _stone = item.InstanceId;
            return "";
        }

        if (!Mixable(item))
        {
            return "Item can't be mixed";
        }

        _item = item.InstanceId;
        return "";
    }

    public static bool Mixable(OwnedItemView item) =>
        item?.Details is { } details && MixingWork.Mixable(item.CatalogKey, item.Kind, IsWearable(item.Kind), details.UpgradeLevel,
            details.AgingGoal > 0, details.MixSheltom);

    public bool Ready(OwnerState state) =>
        _materials.Any(static id => id != Guid.Empty) && (Alchemy || MainItem(state) is not null);

    public long Price(OwnerState state)
    {
        if (!Alchemy)
        {
            return MainItem(state) is { Details: { } details } ? MixingWork.Price(details.Roll.Price, new MixingSettings(_pricePercent)) : 0;
        }

        var settings = new MixingSettings(AlchemyPricePercent: _pricePercent);
        return state?.Items.Where(item => Array.IndexOf(_materials, item.InstanceId) >= 0)
            .Sum(item => Math.Max(0, MixingWork.AlchemyPrice(AgingRules.SheltomOrdinal(item.CatalogKey), settings))) ?? 0;
    }

    public MixFormula Formula(OwnerState state)
    {
        if (MainItem(state) is not { } item)
        {
            return null;
        }

        var counts = new int[MixFormulas.SheltomKinds];
        foreach (var material in state.Items.Where(entry => Array.IndexOf(_materials, entry.InstanceId) >= 0))
        {
            var ordinal = AgingRules.SheltomOrdinal(material.CatalogKey);
            if (ordinal is >= 1 and <= MixFormulas.SheltomKinds)
            {
                counts[ordinal - 1]++;
            }
        }

        return MixFormulas.Find(AgingRules.Family(item.CatalogKey), counts);
    }

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
        painter.Sprite(Alchemy ? HudCraftArt.AlchemyTitle : HudCraftArt.MixTitle, title.X, title.Y);
        if (Alchemy)
        {
            painter.Sprite(HudCraftArt.AlchemyPanel, origin.X + 17, origin.Y + 84);
        }
        else
        {
            var panel = HudCraftLayout.Panel(origin);
            painter.Sprite(HudCraftArt.Panel, panel.X, panel.Y);
        }

        var plate = HudCraftLayout.PricePlate(origin);
        painter.Sprite(HudNpcArt.Gold, plate.X, plate.Y);
        if (incoming is not null && interactive)
        {
            DrawPlacement(painter, origin, incoming, pointer);
        }

        foreach (var (item, bounds) in Placed(state, origin))
        {
            if (interactive && bounds.Contains(pointer))
            {
                _hovered = item;
            }

            HudShopWindow.DrawIcon(painter, icons, item.CatalogKey, bounds);
        }

        if (Ready(state) && _startHovered)
        {
            var start = HudCraftLayout.Start(origin);
            painter.Sprite(HudCraftArt.StartButton, start.X, start.Y);
        }

        if (_exitHovered && interactive)
        {
            var exit = HudStoreLayout.Exit(origin);
            painter.Sprite(HudNpcArt.Exit, exit.X, exit.Y);
        }

        if (Ready(state))
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
            painter.Sprite(Alchemy ? HudCraftArt.AlchemyStartInfo : HudCraftArt.MixStartInfo, Alchemy ? info.X - 10 : info.X, info.Y);
        }
        else if (_hovered is not null)
        {
            HudItemDetailTooltip.Draw(painter, layout, pointer, HudItemDetailTooltip.Lines(_hovered, state, "", "Right-click: take back"),
                HudItemDetailTooltip.Gauge(_hovered, state));
        }
    }

    private bool Accepts(string key)
    {
        var ordinal = AgingRules.SheltomOrdinal(key);
        if (Alchemy)
        {
            return MixingWork.AlchemyPrice(ordinal) >= 0;
        }

        return ordinal > 0 || MixingWork.SealOf(key) is not null;
    }

    private void DrawPlacement(HudPainter painter, Point origin, OwnedItemView incoming, Point pointer)
    {
        var key = incoming.CatalogKey;
        var slot = HudCraftLayout.SheltomAt(origin, pointer);
        if (slot >= 0)
        {
            painter.Fill(HudCraftLayout.Sheltom(origin, slot), Accepts(key) && _materials[slot] == Guid.Empty ? Placement : Refusal);
            return;
        }

        if (Alchemy)
        {
            return;
        }

        var stone = HudCraftLayout.Stone(origin);
        if (stone.Contains(pointer))
        {
            painter.Fill(stone, key.Equals(MixingWork.RespecStone, StringComparison.OrdinalIgnoreCase) ? Placement : Refusal);
            return;
        }

        var box = HudCraftLayout.ItemBox(origin);
        if (box.Contains(pointer))
        {
            painter.Fill(box, Mixable(incoming) ? Placement : Refusal);
        }
    }

    private void Remove(Guid id)
    {
        if (id == _item)
        {
            _item = Guid.Empty;
        }
        else if (id == _stone)
        {
            _stone = Guid.Empty;
        }
        else if (Array.IndexOf(_materials, id) is >= 0 and var slot)
        {
            _materials[slot] = Guid.Empty;
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
        if (slot >= 0 && _materials[slot] != Guid.Empty)
        {
            return _materials[slot];
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
            else if (Array.IndexOf(_materials, item.InstanceId) is >= 0 and var slot)
            {
                yield return (item, HudCraftLayout.Sheltom(origin, slot));
            }
        }
    }

    private OwnedItemView MainItem(OwnerState state) =>
        _item == Guid.Empty ? null : state?.Items.FirstOrDefault(item => item.InstanceId == _item);

    private static bool IsWearable(Simulation.Items.ItemKind kind) => kind is not (Simulation.Items.ItemKind.Potion or
        Simulation.Items.ItemKind.Material or Simulation.Items.ItemKind.Quest or Simulation.Items.ItemKind.Misc or Simulation.Items.ItemKind.Sheltom
        or Simulation.Items.ItemKind.ForceOrb);
}
