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
    // Sets the item down over exactly one other bag item, which takes its place (Inventory.TrySwap).
    Swap,
    // Puts Count of a bag item on the ground where the player stands (handled by the Application, as Pickup is).
    Drop,
    // Potions from the bag or a box into belt box Destination, Count of them (0: as many as fit; PlayerCharacter.TryPutOnBelt).
    BeltPut,
    // A belt box's potions into the bag at Destination (-1: wherever they fit).
    BeltTake
}

public sealed record InventoryCommand(Guid OperationId, long ExpectedRevision, InventoryAction Action,
    Guid ItemId, int Destination, int Count, EquipmentSlot Slot);

public sealed record InventoryOutcome(bool Ok, long Revision, string Message);
public sealed record AppliedInventoryOperation(InventoryCommand Command, long Revision) : IAppliedOperation
{
    Guid IAppliedOperation.OperationId => Command?.OperationId ?? Guid.Empty;
}

public static class InventoryCommands
{
    public static bool Apply(PlayerCharacter character, InventoryCommand command)
    {
        if (command.OperationId == Guid.Empty || command.ItemId == Guid.Empty || !Enum.IsDefined(command.Action)
            || command.ExpectedRevision != character.InventoryRevision)
        {
            return false;
        }

        var anchor = character.Inventory.Find(command.ItemId);
        return command.Action switch
        {
            InventoryAction.Move => character.Inventory.TryMove(anchor, command.Destination),
            InventoryAction.Split => character.Inventory.TrySplit(anchor, command.Destination, command.Count),
            InventoryAction.Merge => character.Inventory.TryMerge(anchor, command.Destination),
            InventoryAction.Swap => character.Inventory.TrySwap(anchor, command.Destination),
            InventoryAction.Equip => Fitted(character, character.TryEquipFromInventory(anchor, command.Slot)),
            InventoryAction.Unequip => character.GetEquipped(command.Slot)?.InstanceId == command.ItemId && Fitted(character, character.TryUnequip(command.Slot)),
            InventoryAction.ConsumePotion => command.Count == 1 && character.Life > 0 && (anchor >= 0
                ? character.Inventory[anchor]?.Definition is { Kind: ItemKind.Potion, Potion.IsValid: true } && character.Inventory.ConsumeAt(anchor)
                : character.TryConsumeFromBelt(command.ItemId)),
            InventoryAction.BeltPut => character.TryPutOnBelt(command.ItemId, command.Destination, command.Count),
            InventoryAction.BeltTake => character.TryTakeFromBelt(command.ItemId, command.Destination),
            _ => false
        };
    }

    // Gear changes the potion space, which the belt then fits (only after a change that went through).
    private static bool Fitted(PlayerCharacter character, bool changed)
    {
        if (changed)
        {
            character.FitBeltToPotionSpace();
        }

        return changed;
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
            default:
                return null;
        }
    }
}
