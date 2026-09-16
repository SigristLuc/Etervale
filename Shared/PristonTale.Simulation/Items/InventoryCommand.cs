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
    Pickup
}

public sealed record InventoryCommand(Guid OperationId, long ExpectedRevision, InventoryAction Action,
    Guid ItemId, int Destination, int Count, EquipmentSlot Slot);

public sealed record InventoryOutcome(bool Ok, long Revision, string Message);
public sealed record AppliedInventoryOperation(InventoryCommand Command, long Revision);

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
            InventoryAction.Equip => character.TryEquipFromInventory(anchor, command.Slot),
            InventoryAction.Unequip => character.GetEquipped(command.Slot)?.InstanceId == command.ItemId && character.TryUnequip(command.Slot),
            InventoryAction.ConsumePotion => command.Count == 1 && anchor >= 0 && character.Life > 0
                && character.Inventory[anchor]?.Definition is { Kind: ItemKind.Potion, Potion.IsValid: true }
                && character.Inventory.ConsumeAt(anchor),
            _ => false
        };
    }
}
