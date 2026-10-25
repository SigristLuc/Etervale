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
    SwapBagPage
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
        return command.Action switch
        {
            InventoryAction.Move => character.Inventory.TryMove(anchor, command.Destination),
            InventoryAction.Split => character.Inventory.TrySplit(anchor, command.Destination, command.Count),
            InventoryAction.Merge => character.Inventory.TryMerge(anchor, command.Destination),
            InventoryAction.Equip => character.TryEquipFromInventory(anchor, command.Slot),
            InventoryAction.Unequip => character.GetEquipped(command.Slot)?.InstanceId == command.ItemId && character.TryUnequip(command.Slot),
            InventoryAction.ConsumePotion => command.Count == 1 && anchor >= 0 && character.Life > 0
                && character.Inventory[anchor]?.Definition is { Kind: ItemKind.Potion, Potion.IsValid: true }
                && character.Inventory.ConsumeAt(anchor),
            InventoryAction.Drop => anchor >= 0 && ItemDropRules.CanDrop(character, character.Inventory[anchor]) && character.Inventory.TakeAt(anchor) is not null,
            InventoryAction.SwapWeaponSet => character.TrySwapWeaponSet(),
            InventoryAction.SwapBagPage => character.TrySwapBagPage(command.ItemId),
            _ => false
        };
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
                var carried = character.Inventory.Find(command.ItemId);
                return carried >= 0 && !ItemDropRules.CanDrop(character, character.Inventory[carried]) ? ItemDropRules.Refusal : null;
            default:
                return null;
        }
    }
}
