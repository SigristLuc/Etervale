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
    public void SwapWeaponSet() => Swap(InventoryAction.SwapWeaponSet);

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

    private void Swap(InventoryAction action)
    {
        if (_state is null || _pendingOperation)
        {
            return;
        }

        CancelDrag();
        SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, action, Guid.Empty, 0, 0, default));
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
                    selected.Slot >= 0 ? InventoryAction.Unequip : InventoryAction.Equip, selected.InstanceId, 0, 0, slot.Value));
            }
        }

        if (_dragging != Guid.Empty && mouse.LeftButton == ButtonState.Released)
        {
            var identity = _dragging;
            var source = _state.Items.FirstOrDefault(item => item.InstanceId == identity);
            // Released on the item it was pressed on: the item stays picked for a click elsewhere (the original's way).
            if (source is not null && ItemBounds(source).Contains(pointer) && ItemBounds(source).Contains(_pressedAt))
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
                SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.Unequip, identity, 0, 0, (EquipmentSlot)source.Slot));
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
                    SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.Equip, identity, 0, 0, (EquipmentSlot)slot));
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
