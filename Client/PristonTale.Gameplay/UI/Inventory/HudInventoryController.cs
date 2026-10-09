using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.UI.Inventory;

public sealed class HudInventoryController(ScreenContext context, Func<InventoryCommand, Task<InventoryOutcome>> applyInventory)
{
    private readonly ScreenContext _context = context;
    private readonly Func<InventoryCommand, Task<InventoryOutcome>> _applyInventory = applyInventory;
    private OwnerState _state;
    private Point _origin;
    private MouseState _previousMouse;
    private Guid _dragging;
    private bool _pendingOperation;
    private volatile InventoryOutcome _completedOperation;

    public string Message { get; private set; } = "";
    public int Completions { get; private set; }
    public bool Pending => _pendingOperation;
    public Guid Dragging => _dragging;
    public Point Origin => _origin;

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
            Completions++;
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

    private void UpdateInventoryInput(KeyboardState keyboard, MouseState mouse, Point pointer, bool clicked)
    {
        var grid = HudInventoryLayout.Bag(_origin);
        OwnedItemView selected = null;
        foreach (var item in _state.Items)
        {
            if (ItemBounds(item).Contains(pointer))
            {
                selected = item;
                break;
            }
        }

        if (clicked && selected is { Slot: < 0 })
        {
            _dragging = selected.InstanceId;
        }

        var rightClicked = mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released;
        if (rightClicked && selected is not null)
        {
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
            _dragging = Guid.Empty;
            if (grid.Contains(pointer))
            {
                var destination = HudInventoryLayout.CellAt(_origin, pointer);
                var source = _state.Items.FirstOrDefault(item => item.InstanceId == identity);
                if (source is not null && source.Anchor != destination)
                {
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
                }
            }
            else
            {
                for (var slot = 0; slot < 10; slot++)
                {
                    if (EquipmentBounds(slot).Contains(pointer))
                    {
                        SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision,
                            InventoryAction.Equip, identity, 0, 0, (EquipmentSlot)slot));
                        break;
                    }
                }
            }
        }
    }

    private void SendInventory(InventoryCommand command)
    {
        _pendingOperation = true;
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

    private EquipmentSlot? SlotFor(ItemKind kind)
    {
        return kind switch
        {
            ItemKind.Sword or ItemKind.Axe or ItemKind.Hammer or ItemKind.Claw or ItemKind.Pole
                or ItemKind.Bow or ItemKind.ThrowingWeapon or ItemKind.Staff or ItemKind.Dagger
                or ItemKind.Phantom => EquipmentSlot.Weapon,
            ItemKind.Armor => EquipmentSlot.Armor,
            ItemKind.Shield => EquipmentSlot.OffHand,
            ItemKind.Gloves => EquipmentSlot.Gloves,
            ItemKind.Boots => EquipmentSlot.Boots,
            ItemKind.Bracelet => EquipmentSlot.Bracelet,
            ItemKind.Amulet => EquipmentSlot.Amulet,
            ItemKind.Sheltom => EquipmentSlot.Sheltom,
            ItemKind.Ring => _state.Items.Any(item => item.Slot == (int)EquipmentSlot.RingLeft) ? EquipmentSlot.RingRight : EquipmentSlot.RingLeft,
            _ => null
        };
    }

}
