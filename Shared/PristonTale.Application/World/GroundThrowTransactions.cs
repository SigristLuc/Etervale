using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Application.World;

// An item a player puts on the ground: taken from the bag (Count of a stack) on a copy of the character, saved, then
// laid where the player stood once the save commits (PreparedThrowOperation).
internal static class GroundThrowTransactions
{
    public static PreparedCharacterOperation<InventoryOutcome> Prepare(PlayerSession session, WorldPlayer player, FieldInstance field, InventoryCommand command)
    {
        var original = player.Character;
        var anchor = original.Inventory.Find(command.ItemId);
        if (command.ExpectedRevision != original.InventoryRevision || anchor < 0 || original.Inventory[anchor] is not { } item
            || command.Count < 1 || command.Count > item.Count)
        {
            return Refused(original, "invalid operation or stale inventory revision");
        }

        if (!ItemDropRules.CanDrop(original, item))
        {
            return Refused(original, ItemRuleMessages.CannotDrop);
        }

        if (field.GroundLoot.Count >= FieldGroundLoot.Capacity)
        {
            return Refused(original, "there is no room on the ground");
        }

        var candidate = original.CloneForInventoryOperation();
        var thrown = candidate.Inventory.TakeFrom(anchor, command.Count);
        candidate.RecordInventoryOperation(command);
        var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, field.FieldId, player.Position);
        return new PreparedThrowOperation(original, candidate, snapshot, new(true, candidate.InventoryRevision, string.Empty), field,
            thrown.Copy(thrown.Count, preserveId: true), player.Position, player.EntityId);
    }

    private static PreparedCharacterOperation<InventoryOutcome> Refused(PlayerCharacter original, string message) =>
        new(original, null, null, new(false, original.InventoryRevision, message));
}
