using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Gameplay.UI.Inventory;

public sealed class HudInventoryController(ScreenContext context, Func<InventoryCommand, Task<InventoryOutcome>> applyInventory)
{
    private readonly ScreenContext _context = context;
    private readonly Func<InventoryCommand, Task<InventoryOutcome>> _applyInventory = applyInventory;
    private OwnerState _state;
    private Point _origin;
    private MouseState _previousMouse;
    private Guid _dragging;
    private bool _held;
    private Point _pressedAt;
    private string _expectedKey;
    private HashSet<Guid> _expectedKnown;
    private long _expectedFrom;
    private long _expectedUntilMs;
    private Guid _holdAfter;
    private bool _pendingOperation;
    private volatile InventoryOutcome _completedOperation;

    public string Message { get; private set; } = "";

    // Whether the last completed operation succeeded (its message is a confirmation rather than a refusal).
    public bool LastOk { get; private set; }
    public int Completions { get; private set; }
    public bool Pending => _pendingOperation;
    public Guid Dragging => _dragging;

    // An item put down and waiting for the server's answer: it stays out of the grid until the answer places it.
    public Guid Settling { get; private set; }
    private long _settledRevision = long.MinValue;
    public Point Origin => _origin;

    // Shop and warehouse windows take clicks on owned items and drops outside the inventory while they are open.
    public Func<OwnedItemView, bool> InterceptClick { get; set; }
    public Func<Guid, Point, bool> ExternalDrop { get; set; }

    // A right click on a carried usable item (premium, travel and effect items).
    public Action<OwnedItemView> UseRequested { get; set; }

    // Whether a picked item released outside the inventory window lands on the ground this frame: no shop, warehouse or
    // other window under the pointer.
    public bool GroundDropAllowed { get; set; }
    // The catalog key of an item lifted from or set into the pack or the equipment, for its sound.
    public Action<string> ItemSound { get; set; }
    private string _pendingSound;

    public void CancelDrag()
    {
        _dragging = Guid.Empty;
        _held = false;
    }

    // Trades the worn weapon and off-hand item with the other set (the W key and the A/B arrows).
    // The next bag item of this catalog key that was not carried before (a picked-up ground item) comes to the cursor once
    // the owner state shows it; a pickup merged into a stack brings nothing.
    public void HoldNextNew(string catalogKey)
    {
        if (_state is null || _dragging != Guid.Empty)
        {
            return;
        }

        _expectedKey = catalogKey;
        _expectedKnown = _state.Items.Select(static item => item.InstanceId).ToHashSet();
        _expectedFrom = _state.InventoryRevision;
        _expectedUntilMs = Environment.TickCount64 + ExpectedItemWaitMs;
    }

    // A refused pickup changes nothing; the expectation lapses instead of catching a later item of the same key.
    private const long ExpectedItemWaitMs = 5000;

    private void TakeExpectedItem(OwnerState state)
    {
        if (_expectedKey is not null && Environment.TickCount64 > _expectedUntilMs)
        {
            _expectedKey = null;
            _expectedKnown = null;
        }

        if (_expectedKey is null || state.InventoryRevision == _expectedFrom)
        {
            return;
        }

        var picked = _dragging == Guid.Empty && !_pendingOperation
            ? state.Items.FirstOrDefault(item => item.Slot < 0 && !_expectedKnown.Contains(item.InstanceId)
                && string.Equals(item.CatalogKey, _expectedKey, StringComparison.OrdinalIgnoreCase))
            : null;
        if (picked is not null)
        {
            _dragging = picked.InstanceId;
            _held = true;
        }

        _expectedKey = null;
        _expectedKnown = null;
    }

    // The item an equip put out of its slot comes to the cursor once the owner state shows it in the bag, as the original
    // swaps the cursor item with the worn one.
    private void TakeDisplacedItem(OwnerState state)
    {
        if (_holdAfter == Guid.Empty || _pendingOperation || state.InventoryRevision < _settledRevision)
        {
            return;
        }

        if (_dragging == Guid.Empty && state.Items.FirstOrDefault(item => item.InstanceId == _holdAfter) is { Slot: < 0 } displaced)
        {
            _dragging = displaced.InstanceId;
            _held = true;
        }

        _holdAfter = Guid.Empty;
    }

    // A worn two-handed weapon also shows in the empty off-hand slot, as the original draws it in both hands.
    public static OwnedItemView TwoHandedShadow(OwnerState state)
    {
        var weapon = state?.Items.FirstOrDefault(static item => item.Slot == (int)EquipmentSlot.Weapon);
        return weapon is not null && ItemEquipmentMetadata.IsTwoHanded(weapon.CatalogKey)
            && !state.Items.Any(static item => item.Slot == (int)EquipmentSlot.OffHand)
            ? weapon
            : null;
    }

    // Whether the pointer is on the item, counting a worn two-handed weapon's off-hand slot.
    private bool Covers(OwnedItemView item, Point pointer) =>
        ItemBounds(item).Contains(pointer)
        || item.InstanceId == TwoHandedShadow(_state)?.InstanceId && EquipmentBounds((int)EquipmentSlot.OffHand).Contains(pointer);

    // A held item stays on the cursor: a bag item is untouched by the swap, and a held worn weapon or off-hand item goes
    // along to the other set, so it can be put down there.
    public void SwapWeaponSet()
    {
        var held = _state?.Items.FirstOrDefault(item => item.InstanceId == _dragging);
        var carried = held is { Slot: (int)EquipmentSlot.Weapon or (int)EquipmentSlot.OffHand } ? held.InstanceId : Guid.Empty;
        Swap(InventoryAction.SwapWeaponSet, keepHeld: held is not null, carried);
    }

    // Shows the other bag page (the E key and the I/II arrows); a bag item held on the cursor goes along and stays held.
    public void SwapBagPage()
    {
        var carried = _state?.Items.FirstOrDefault(item => item.InstanceId == _dragging && item.Slot < 0);
        if (carried is null)
        {
            Swap(InventoryAction.SwapBagPage);
            return;
        }

        if (_pendingOperation)
        {
            return;
        }

        _held = true;
        SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.SwapBagPage, carried.InstanceId, 0, 0, default));
    }

    private void Swap(InventoryAction action, bool keepHeld = false, Guid carried = default)
    {
        if (_state is null || _pendingOperation)
        {
            return;
        }

        if (keepHeld)
        {
            _held = true;
        }
        else
        {
            CancelDrag();
        }

        SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, action, carried, 0, 0, default));
    }

    public void Update(OwnerState state, HudLayout layout, bool visible, KeyboardState keyboard, MouseState mouse)
    {
        _state = state;
        _origin = HudInventoryLayout.Origin(layout);
        var completed = _completedOperation;
        if (completed is not null)
        {
            _completedOperation = null;
            _pendingOperation = false;
            _settledRevision = completed.Ok ? completed.Revision : long.MinValue;
            if (!completed.Ok)
            {
                _holdAfter = Guid.Empty;
            }
            Message = completed.Message;
            LastOk = completed.Ok;
            Completions++;
            if (completed.Ok && _pendingSound is not null)
            {
                ItemSound?.Invoke(_pendingSound);
            }

            _pendingSound = null;
        }

        if (Settling != Guid.Empty && !_pendingOperation && (state is null || state.InventoryRevision >= _settledRevision))
        {
            Settling = Guid.Empty;
        }

        if (visible && state is not null)
        {
            TakeExpectedItem(state);
            TakeDisplacedItem(state);
        }

        if (!visible || state is null)
        {
            CancelDrag();
        }
        else if (!_pendingOperation)
        {
            var clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            UpdateInventoryInput(keyboard, mouse, layout.ToLogical(mouse.Position), clicked);
        }

        _previousMouse = mouse;
    }

    private void UpdateInventoryInput(KeyboardState keyboard, MouseState mouse, Point pointer, bool clicked)
    {
        OwnedItemView selected = null;
        foreach (var item in _state.Items)
        {
            if (ItemBounds(item).Contains(pointer))
            {
                selected = item;
                break;
            }
        }

        if (selected is null && TwoHandedShadow(_state) is { } shadow && Covers(shadow, pointer))
        {
            selected = shadow;
        }

        var rightClicked = mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released;
        if (_dragging != Guid.Empty && (rightClicked || keyboard.IsKeyDown(Keys.Escape)))
        {
            CancelDrag();
            return;
        }

        // A picked item follows the cursor until the next click places it.
        if (_held)
        {
            if (clicked)
            {
                var identity = _dragging;
                CancelDrag();
                Place(identity, pointer, selected, keyboard);
            }

            return;
        }

        if (clicked && selected is null && HudInventoryLayout.WeaponSet(_origin, 1 - _state.WeaponSet).Contains(pointer))
        {
            SwapWeaponSet();
            return;
        }

        if (clicked && selected is null && HudInventoryLayout.BagPage(_origin, 1 - _state.BagPage).Contains(pointer))
        {
            SwapBagPage();
            return;
        }

        if (clicked && selected is not null && InterceptClick?.Invoke(selected) == true)
        {
            return;
        }

        if (clicked && selected is not null)
        {
            _dragging = selected.InstanceId;
            _pressedAt = pointer;
            ItemSound?.Invoke(selected.CatalogKey);
        }

        if (rightClicked && selected is not null)
        {
            if (selected.Slot < 0 && UsableItemCatalog.IsUsable(selected.CatalogKey))
            {
                CancelDrag();
                UseRequested?.Invoke(selected);
                return;
            }

            if (selected.Slot < 0 && selected.Kind == ItemKind.Potion)
            {
                CancelDrag();
                SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision,
                    InventoryAction.ConsumePotion, selected.InstanceId, 0, 1, default));
                return;
            }

            var slot = selected.Slot >= 0 ? (EquipmentSlot?)selected.Slot : SlotFor(selected.Kind);
            if (slot.HasValue)
            {
                SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision,
                    selected.Slot >= 0 ? InventoryAction.Unequip : InventoryAction.Equip, selected.InstanceId, -1, 0, slot.Value));
            }
        }

        if (_dragging != Guid.Empty && mouse.LeftButton == ButtonState.Released)
        {
            var identity = _dragging;
            var source = _state.Items.FirstOrDefault(item => item.InstanceId == identity);
            // Released on the item it was pressed on: the item stays picked for a click elsewhere (the original's way).
            if (source is not null && Covers(source, pointer) && Covers(source, _pressedAt))
            {
                _held = true;
                return;
            }

            CancelDrag();
            Place(identity, pointer, selected, keyboard);
        }
    }

    // A picked or dragged item put down under the pointer: a bag cell moves, merges, splits (Shift) or unequips, an
    // equipment slot equips, the shop and warehouse take it, and anywhere else outside the window throws it on the ground.
    private void Place(Guid identity, Point pointer, OwnedItemView selected, KeyboardState keyboard)
    {
        var source = _state.Items.FirstOrDefault(item => item.InstanceId == identity);
        if (source is null)
        {
            return;
        }

        PlaceCommand(identity, source, pointer, selected, keyboard);
        if (_pendingOperation)
        {
            Settling = identity;
        }
    }

    private void PlaceCommand(Guid identity, OwnedItemView source, Point pointer, OwnedItemView selected, KeyboardState keyboard)
    {
        if (HudInventoryLayout.Bag(_origin).Contains(pointer))
        {
            if (source.Slot >= 0)
            {
                SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.Unequip, identity,
                    HudInventoryLayout.CellAt(_origin, pointer), 0, (EquipmentSlot)source.Slot));
                return;
            }

            var destination = HudInventoryLayout.CellAt(_origin, pointer);
            if (source.Anchor == destination)
            {
                return;
            }

            var split = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            var action = InventoryAction.Move;
            if (split)
            {
                action = InventoryAction.Split;
            }
            else if (selected is not null && selected.InstanceId != identity)
            {
                action = InventoryAction.Merge;
            }

            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, action, identity,
                selected is not null && action == InventoryAction.Merge ? selected.Anchor : destination, source.Count / 2, default));
            return;
        }

        for (var slot = 0; slot < 10; slot++)
        {
            if (EquipmentBounds(slot).Contains(pointer))
            {
                if (source.Slot < 0)
                {
                    // A two-handed weapon goes to the weapon hand whichever hand slot takes it; the item worn in that
                    // slot (or the two-handed weapon an off-hand item replaces) comes to the cursor.
                    var target = slot == (int)EquipmentSlot.OffHand && ItemEquipmentMetadata.IsTwoHanded(source.CatalogKey) ? (int)EquipmentSlot.Weapon : slot;
                    var worn = _state.Items.FirstOrDefault(item => item.Slot == target)
                        ?? (target == (int)EquipmentSlot.OffHand ? TwoHandedShadow(_state) : null);
                    _holdAfter = worn?.InstanceId ?? Guid.Empty;
                    SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.Equip, identity, 0, 0, (EquipmentSlot)target));
                }

                return;
            }
        }

        if (source.Slot >= 0 || ExternalDrop?.Invoke(identity, pointer) == true)
        {
            return;
        }

        if (!HudInventoryLayout.Window(_origin).Contains(pointer) && GroundDropAllowed)
        {
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.Drop, identity, 0, source.Count, default));
        }
    }

    private void SendInventory(InventoryCommand command)
    {
        _pendingOperation = true;
        _pendingSound = command.Action == InventoryAction.ConsumePotion
            ? null
            : _state.Items.FirstOrDefault(item => item.InstanceId == command.ItemId)?.CatalogKey;
        _ = CompleteInventoryAsync(command);
    }

    private async Task CompleteInventoryAsync(InventoryCommand command)
    {
        try
        {
            _completedOperation = await _applyInventory(command).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _context.Log.Error("inventory operation failed", exception);
            _completedOperation = new InventoryOutcome(false, 0, "Operation failed; reconnect to verify saved state.");
        }
    }

    public Rectangle EquipmentBounds(int slot) => HudInventoryLayout.Equipment(_origin, slot);

    public Rectangle ItemBounds(OwnedItemView item) =>
        item.Slot >= 0 ? EquipmentBounds(item.Slot) : HudInventoryLayout.Cell(_origin, item.Anchor, item.Width, item.Height);

    private EquipmentSlot? SlotFor(ItemKind kind) =>
        ItemEquipmentMetadata.DefaultSlot(kind, ItemEquipmentMetadata.SlotOf(kind), slot => _state.Items.Any(item => item.Slot == (int)slot));

}
