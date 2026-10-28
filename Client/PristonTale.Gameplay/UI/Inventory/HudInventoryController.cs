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
    // The item on the cursor (it keeps its place on the server until it is set down), and the one a pending command puts
    // there once it succeeds: the item an exchange picks up, or an equipped item taken off.
    private Guid _dragging;
    private Guid _carryAfter;
    private bool _pendingOperation;
    private volatile InventoryOutcome _completedOperation;

    public string Message { get; private set; } = "";

    // Whether the last completed operation succeeded (its message is a confirmation rather than a refusal).
    public bool LastOk { get; private set; }
    public int Completions { get; private set; }
    public bool Pending => _pendingOperation;
    public Guid Dragging => _dragging;

    // Set by the HUD each frame: nothing is picked up while the player attacks or casts, as in the original.
    public bool PickupLocked { get; set; }
    public Point Origin => _origin;

    // Shop and warehouse windows take clicks on owned items and drops outside the inventory while they are open.
    public Func<OwnedItemView, bool> InterceptClick { get; set; }
    public Func<Guid, Point, bool> ExternalDrop { get; set; }

    // A right click on a carried usable item (premium, travel and effect items).
    public Action<OwnedItemView> UseRequested { get; set; }

    // The catalog key of an item lifted from or set into the pack or the equipment, for its sound.
    public Action<string> ItemSound { get; set; }
    private string _pendingSound;

    public void CancelDrag()
    {
        _dragging = Guid.Empty;
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
            Message = completed.Message;
            LastOk = completed.Ok;
            Completions++;
            if (completed.Ok && _pendingSound is not null)
            {
                ItemSound?.Invoke(_pendingSound);
            }

            if (completed.Ok && _carryAfter != Guid.Empty && state?.Items.Any(item => item.InstanceId == _carryAfter && item.Slot < 0) == true)
            {
                _dragging = _carryAfter;
            }

            _pendingSound = null;
            _carryAfter = Guid.Empty;
        }

        if (visible && state is not null && !_pendingOperation)
        {
            var clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            UpdateInventoryInput(keyboard, mouse, layout.ToLogical(mouse.Position), clicked);
        }
        else
        {
            CancelDrag();
        }

        _previousMouse = mouse;
    }

    // The original's carrying: a click picks an item up (an equipped one is taken off first) and it follows the cursor; the
    // next click sets it down where HudInventoryPlacement says, or takes it out of the bag above the window.
    private void UpdateInventoryInput(KeyboardState keyboard, MouseState mouse, Point pointer, bool clicked)
    {
        if (_dragging != Guid.Empty)
        {
            var carried = _state.Items.FirstOrDefault(item => item.InstanceId == _dragging);
            if (carried is null)
            {
                _dragging = Guid.Empty;
            }
            else if (clicked)
            {
                SetDown(carried, pointer, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
            }

            return;
        }

        var hovered = _state.Items.FirstOrDefault(item => ItemBounds(item).Contains(pointer));
        if (hovered is null)
        {
            return;
        }

        if (clicked)
        {
            if (InterceptClick?.Invoke(hovered) != true && !PickupLocked)
            {
                PickUp(hovered);
            }

            return;
        }

        if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released)
        {
            Use(hovered);
        }
    }

    private void PickUp(OwnedItemView item)
    {
        if (item.Slot >= 0)
        {
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.Unequip, item.InstanceId, 0, 0,
                (EquipmentSlot)item.Slot), carryAfter: item.InstanceId);
            return;
        }

        _dragging = item.InstanceId;
        ItemSound?.Invoke(item.CatalogKey);
    }

    private void SetDown(OwnedItemView carried, Point pointer, bool split)
    {
        var placement = HudInventoryPlacement.Of(_origin, _state.Items, carried, pointer, HudInventoryPanel.Usable(carried, _state));
        switch (placement.Kind)
        {
            case HudPlacementKind.Fits or HudPlacementKind.Exchange when placement.Slot is { } slot:
                Send(InventoryAction.Equip, carried, 0, 0, slot, placement.Covered);
                break;
            case HudPlacementKind.Fits when placement.Anchor == carried.Anchor:
                _dragging = Guid.Empty;
                ItemSound?.Invoke(carried.CatalogKey);
                break;
            case HudPlacementKind.Fits when split && carried.Count > 1:
                Send(InventoryAction.Split, carried, placement.Anchor, carried.Count / 2);
                break;
            case HudPlacementKind.Fits:
                Send(InventoryAction.Move, carried, placement.Anchor);
                break;
            case HudPlacementKind.Merge:
                Send(InventoryAction.Merge, carried, placement.Covered.Anchor);
                break;
            case HudPlacementKind.Exchange:
                Send(InventoryAction.Swap, carried, placement.Anchor, covered: placement.Covered);
                break;
            case HudPlacementKind.Outside when ExternalDrop?.Invoke(carried.InstanceId, pointer) == true:
                _dragging = Guid.Empty;
                break;
        }
    }

    private void Send(InventoryAction action, OwnedItemView carried, int destination, int count = 0, EquipmentSlot slot = default,
        OwnedItemView covered = null)
    {
        _dragging = Guid.Empty;
        SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, action, carried.InstanceId, destination, count, slot),
            covered?.InstanceId ?? Guid.Empty);
    }

    // Right click: a usable item is used, a potion drunk, and an item worn or taken off.
    private void Use(OwnedItemView item)
    {
        if (item.Slot < 0 && UsableItemCatalog.IsUsable(item.CatalogKey))
        {
            UseRequested?.Invoke(item);
            return;
        }

        if (item.Slot < 0 && item.Kind == ItemKind.Potion)
        {
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.ConsumePotion, item.InstanceId, 0, 1, default));
            return;
        }

        var slot = item.Slot >= 0 ? (EquipmentSlot?)item.Slot : SlotFor(item.Kind);
        if (slot.HasValue)
        {
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision,
                item.Slot >= 0 ? InventoryAction.Unequip : InventoryAction.Equip, item.InstanceId, 0, 0, slot.Value));
        }
    }

    private void SendInventory(InventoryCommand command, Guid carryAfter = default)
    {
        _pendingOperation = true;
        _carryAfter = carryAfter;
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
