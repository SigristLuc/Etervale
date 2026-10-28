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
    // The item on the cursor (it keeps its place on the server until it is set down). _carryOnSuccess: what the pending
    // command hands to the cursor when it succeeds (the item an exchange picks up, an equipped item taken off).
    // _carryAfter: an item carried once the owner state reaches the inventory revision _carryAt and shows it in the bag
    // (the state travels apart from the replies).
    private Guid _dragging;
    private Guid _carryOnSuccess;
    private Guid _carryAfter;
    private long _carryAt;
    // The item under the pointer this frame (the window open), for Shift and a belt key.
    private OwnedItemView _hovered;
    // A weapon pair asked for while the player swings or casts: held once the motion ends, as the original waits for it.
    private int _pairWanted = -1;
    private bool _pendingOperation;
    private volatile InventoryOutcome _completedOperation;

    public string Message { get; private set; } = "";

    // Whether the last completed operation succeeded (its message is a confirmation rather than a refusal).
    public bool LastOk { get; private set; }
    public int Completions { get; private set; }
    public Guid Dragging => _dragging;

    // The owned item under the pointer while the window takes input and nothing is carried.
    public OwnedItemView Hovered => _hovered;

    // Set by the HUD each frame: nothing is picked up while the player attacks or casts, as in the original.
    public bool PickupLocked { get; set; }

    // Shop and warehouse windows take clicks on owned items and drops outside the inventory while they are open.
    public Func<OwnedItemView, bool> InterceptClick { get; set; }
    public Func<Guid, Point, bool> ExternalDrop { get; set; }

    // An item set down above the window where no shop or warehouse takes it: put on the ground (true when the HUD took it).
    public Func<OwnedItemView, bool> ThrowRequested { get; set; }

    // The coin by the gold, clicked with nothing carried.
    public Action GoldRequested { get; set; }

    // A right click on a carried usable item (premium, travel and effect items).
    public Action<OwnedItemView> UseRequested { get; set; }

    // The catalog key of an item lifted from or set into the pack or the equipment, for its sound.
    public Action<string> ItemSound { get; set; }
    private string _pendingSound;

    public void CancelDrag()
    {
        _dragging = Guid.Empty;
    }

    public void CarryAfter(Guid itemId, long revision)
    {
        _carryAfter = itemId;
        _carryAt = revision;
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

            if (completed.Ok && _carryOnSuccess != Guid.Empty)
            {
                CarryAfter(_carryOnSuccess, completed.Revision);
            }

            _carryOnSuccess = Guid.Empty;
            _pendingSound = null;
        }

        if (_carryAfter != Guid.Empty && state is not null && state.InventoryRevision >= _carryAt)
        {
            if (OwnedItemView.Find(state.Items, _carryAfter) is { InBag: true })
            {
                _dragging = _carryAfter;
            }

            _carryAfter = Guid.Empty;
        }

        if (_pairWanted >= 0 && !PickupLocked && !_pendingOperation && state is not null)
        {
            if (_pairWanted != state.WeaponSet)
            {
                SendInventory(new InventoryCommand(Guid.NewGuid(), state.InventoryRevision, InventoryAction.SwitchWeaponSet, Guid.Empty, _pairWanted, 0, default));
            }

            _pairWanted = -1;
        }

        // The hover shows while a command is pending too; only the clicks wait for it.
        _hovered = visible && state is not null && _dragging == Guid.Empty ? HudInventoryLayout.ItemAt(_origin, state.Items, layout.ToLogical(mouse.Position)) : null;
        if (visible && state is not null && !_pendingOperation)
        {
            var clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            UpdateInventoryInput(keyboard, mouse, layout.ToLogical(mouse.Position), clicked);
        }
        else
        {
            CancelDrag();
            // The belt is on the bottom bar: its potions are drunk with a right click while the window is closed too.
            var pointer = layout.ToLogical(mouse.Position);
            if (state is not null && !_pendingOperation && mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released
                && HudInventoryLayout.ItemAt(_origin, state.Items, pointer) is { BeltBox: >= 0 } potion)
            {
                Use(potion);
            }
        }

        _previousMouse = mouse;
    }

    // The original's carrying: a click picks an item up (an equipped one is taken off first) and it follows the cursor; the
    // next click sets it down where HudInventoryPlacement says, or takes it out of the bag above the window.
    private void UpdateInventoryInput(KeyboardState keyboard, MouseState mouse, Point pointer, bool clicked)
    {
        if (_dragging != Guid.Empty)
        {
            var carried = OwnedItemView.Find(_state.Items, _dragging);
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

        if (clicked)
        {
            for (var side = 0; side < PlayerCharacter.Sides; side++)
            {
                if (HudInventoryLayout.PageButton(_origin, side).Contains(pointer))
                {
                    ShowBagPage(side);
                    return;
                }

                if (HudInventoryLayout.PairButton(_origin, side).Contains(pointer))
                {
                    HoldWeaponPair(side);
                    return;
                }
            }

            if (HudInventoryLayout.GoldButton(_origin).Contains(pointer))
            {
                GoldRequested?.Invoke();
                return;
            }
        }

        var hovered = _hovered;
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
        if (item.Equipped)
        {
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.Unequip, item.InstanceId, 0, 0,
                (EquipmentSlot)item.Slot), carryOnSuccess: item.InstanceId);
            return;
        }

        _dragging = item.InstanceId;
        ItemSound?.Invoke(item.CatalogKey);
    }

    private void SetDown(OwnedItemView carried, Point pointer, bool split)
    {
        var placement = HudInventoryPlacement.Of(_origin, _state.Items, carried, pointer, HudInventoryPanel.Usable(carried, _state));
        if (placement.Belt >= 0 || carried.BeltBox >= 0)
        {
            SetDownPotion(carried, placement);
            return;
        }

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
            case HudPlacementKind.Outside when ThrowRequested?.Invoke(carried) == true:
                _dragging = Guid.Empty;
                break;
        }
    }

    // The belt's side of setting down: into a box (a bag potion over another kind's box picks that stack up in exchange),
    // or from a box back to free bag cells or onto a bag stack of its kind.
    private void SetDownPotion(OwnedItemView carried, HudPlacement placement)
    {
        if (placement.Belt >= 0 && placement.Belt == carried.BeltBox)
        {
            _dragging = Guid.Empty;
            ItemSound?.Invoke(carried.CatalogKey);
        }
        else if (placement.Belt >= 0 && placement.Kind != HudPlacementKind.Blocked)
        {
            var displaced = placement.Kind == HudPlacementKind.Exchange && carried.InBag ? placement.Covered : null;
            Send(InventoryAction.BeltPut, carried, placement.Belt, covered: displaced);
        }
        else if (placement.Kind == HudPlacementKind.Fits)
        {
            Send(InventoryAction.BeltTake, carried, placement.Anchor);
        }
        else if (placement.Kind == HudPlacementKind.Merge)
        {
            Send(InventoryAction.BeltTake, carried, placement.Covered.Anchor);
        }
    }

    // The bag page and the weapon pair (the side buttons, keys E and W): not while an item is carried or a command waits.
    public void ShowBagPage(int page)
    {
        if (!_pendingOperation && _state is not null && _dragging == Guid.Empty && page != _state.BagPage)
        {
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.SwitchBagPage, Guid.Empty, page, 0, default));
        }
    }

    public void HoldWeaponPair(int pair)
    {
        if (_state is not null && _dragging == Guid.Empty && pair != _state.WeaponSet)
        {
            _pairWanted = pair;
        }
    }

    public void ToggleBagPage() => ShowBagPage(1 - (_state?.BagPage ?? 1));

    public void ToggleWeaponPair() => HoldWeaponPair(1 - (_state?.WeaponSet ?? 1));

    // Keys 1 to 3, as in the original: drink the box's potion; with Shift, fill the box from the bag potion under the
    // pointer.
    public void BeltKey(int box, bool shift)
    {
        if (_pendingOperation || _state is null || _dragging != Guid.Empty)
        {
            return;
        }

        if (shift)
        {
            if (_hovered is { InBag: true, Kind: ItemKind.Potion } potion)
            {
                SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.BeltPut, potion.InstanceId, box, 0, default));
            }

            return;
        }

        if (OwnedItemView.InBox(_state.Items, box) is { } held)
        {
            Use(held);
        }
    }

    private void Send(InventoryAction action, OwnedItemView carried, int destination, int count = 0, EquipmentSlot slot = default,
        OwnedItemView covered = null)
    {
        _dragging = Guid.Empty;
        SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, action, carried.InstanceId, destination, count, slot),
            covered?.InstanceId ?? Guid.Empty);
    }

    // Puts `count` of a bag item, or with no item `count` gold, on the ground where the player stands.
    public void Drop(OwnedItemView item, int count)
    {
        if (!_pendingOperation && _state is not null)
        {
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, item is null ? InventoryAction.DropGold : InventoryAction.Drop,
                item?.InstanceId ?? Guid.Empty, 0, count, default));
        }
    }

    // Right click: a usable item is used, a potion drunk, and an item worn or taken off.
    private void Use(OwnedItemView item)
    {
        if (item.InBag && UsableItemCatalog.IsUsable(item.CatalogKey))
        {
            UseRequested?.Invoke(item);
            return;
        }

        if (!item.Equipped && item.Kind == ItemKind.Potion)
        {
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.ConsumePotion, item.InstanceId, 0, 1, default));
            return;
        }

        var slot = item.Equipped ? (EquipmentSlot?)item.Slot : SlotFor(item.Kind);
        if (slot.HasValue)
        {
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision,
                item.Equipped ? InventoryAction.Unequip : InventoryAction.Equip, item.InstanceId, 0, 0, slot.Value));
        }
    }

    private void SendInventory(InventoryCommand command, Guid carryOnSuccess = default)
    {
        _pendingOperation = true;
        _carryOnSuccess = carryOnSuccess;
        _pendingSound = command.Action switch
        {
            InventoryAction.ConsumePotion => null,
            InventoryAction.DropGold => ItemCodes.GoldKey,
            _ => OwnedItemView.Find(_state.Items, command.ItemId)?.CatalogKey
        };
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

    private EquipmentSlot? SlotFor(ItemKind kind) =>
        ItemEquipmentMetadata.DefaultSlot(kind, ItemEquipmentMetadata.SlotOf(kind), slot => OwnedItemView.InSlot(_state.Items, (int)slot) is not null);

}
