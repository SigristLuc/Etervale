using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Items;

public enum InventoryAction
{
    Move,
    Split,
    Merge,
    Equip,
    Unequip,
    ConsumePotion,
    Pickup,
    Drop,
    SwapWeaponSet,
    SwapBagPage,
    // Destination is the belt slot (0..2).
    ToBelt,
    // Destination is the bag anchor, or -1 for the first place that takes the stack.
    FromBelt
}

public sealed record InventoryCommand(Guid OperationId, long ExpectedRevision, InventoryAction Action,
    Guid ItemId, int Destination, int Count, EquipmentSlot Slot);

public sealed record InventoryOutcome(bool Ok, long Revision, string Message);
public sealed record AppliedInventoryOperation(InventoryCommand Command, long Revision);

public static class InventoryCommands
{
    public static bool Apply(PlayerCharacter character, InventoryCommand command)
    {
        // Swapping weapon sets or bag pages names no item.
        if (command.OperationId == Guid.Empty || command.ItemId == Guid.Empty && command.Action is not (InventoryAction.SwapWeaponSet or InventoryAction.SwapBagPage)
            || !Enum.IsDefined(command.Action) || command.ExpectedRevision != character.InventoryRevision)
        {
            return false;
        }

        var anchor = character.Inventory.Find(command.ItemId);
        var applied = command.Action switch
        {
            InventoryAction.Move => character.Inventory.TryMove(anchor, command.Destination),
            InventoryAction.Split => character.Inventory.TrySplit(anchor, command.Destination, command.Count),
            InventoryAction.Merge => character.Inventory.TryMerge(anchor, command.Destination),
            InventoryAction.Equip => character.TryEquipFromInventory(anchor, command.Slot),
            InventoryAction.Unequip => character.GetEquipped(command.Slot)?.InstanceId == command.ItemId && character.TryUnequip(command.Slot, command.Destination),
            InventoryAction.ConsumePotion => command.Count == 1 && character.Life > 0 && character.TryConsumePotion(command.ItemId),
            InventoryAction.Drop => character.TryDropCarried(command.ItemId),
            InventoryAction.SwapWeaponSet => character.TrySwapWeaponSet(command.ItemId),
            InventoryAction.SwapBagPage => character.TrySwapBagPage(command.ItemId),
            InventoryAction.ToBelt => character.TryMoveToBelt(command.ItemId, command.Destination),
            InventoryAction.FromBelt => character.TryMoveFromBelt(command.ItemId, command.Destination),
            _ => false
        };

        // Equipment changes can lower the belt capacity.
        if (applied && command.Action is InventoryAction.Equip or InventoryAction.Unequip or InventoryAction.SwapWeaponSet)
        {
            character.SettleBelt();
        }

        return applied;
    }

    // The item rule that refuses a command, for the player's message; null when the command fails for another reason.
    public static string Refusal(PlayerCharacter character, InventoryCommand command)
    {
        switch (command.Action)
        {
            case InventoryAction.Equip:
                var anchor = character.Inventory.Find(command.ItemId);
                return anchor >= 0 && character.Inventory[anchor] is { } item && item.Definition.IsEquippable && !ItemUsability.CanUse(character, item)
                    ? ItemRuleMessages.CannotUse
                    : null;
            case InventoryAction.Unequip:
                return CarriedWeight.IsOverweight(character) ? ItemRuleMessages.OverWeight : null;
            case InventoryAction.Drop:
                return character.FindCarried(command.ItemId) is { } carried && !ItemDropRules.CanDrop(character, carried) ? ItemDropRules.Refusal : null;
            case InventoryAction.SwapWeaponSet:
                return command.ItemId != Guid.Empty && character.CarriedHandHasNoRoom(command.ItemId) ? ItemRuleMessages.NoRoomInBag : null;
            case InventoryAction.SwapBagPage:
                var held = character.Inventory.Find(command.ItemId);
                return command.ItemId != Guid.Empty && held >= 0 && !character.SpareBag.HasRoomFor(character.Inventory[held])
                    ? ItemRuleMessages.NoRoomOnOtherPage
                    : null;
            case InventoryAction.ToBelt:
                return character.FindCarried(command.ItemId) is { } potion && PlayerCharacter.BeltHolds(potion) && command.Destination is >= 0 and < PlayerCharacter.BeltSlots
                    && character.Belt[command.Destination] is { } slotted && Inventory.CanStack(slotted, potion) && slotted.Count >= character.Derived.PotionSpace
                    ? ItemRuleMessages.BeltFull
                    : null;
            case InventoryAction.FromBelt:
                return command.Destination < 0 && character.Belt.Find(command.ItemId) >= 0 ? ItemRuleMessages.NoRoomInBag : null;
            default:
                return null;
        }
    }
}
