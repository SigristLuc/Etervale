using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Application.World;

internal static class GroundPickupTransactions
{
    public static PreparedCharacterOperation<InventoryOutcome> Prepare(PlayerSession session, WorldPlayer player, FieldInstance field, InventoryCommand command)
    {
        var original = player.Character;
        var dropId = field.GroundLoot.ResolveClientId(command.ItemId);
        if (command.ExpectedRevision != original.InventoryRevision || command.Count != 1
            || !field.GroundLoot.TryReserve(dropId, command.OperationId, player, out var drop, out var item))
        {
            return new(original, null, null, new(false, original.InventoryRevision, "drop unavailable or out of reach"));
        }

        bool prepared = false;
        try
        {
            var candidate = original.CloneForInventoryOperation();
            if (!candidate.ReceiveGroundLoot(item, drop!.Gold ? drop.Amount : 0))
            {
                return new(original, null, null, new(false, original.InventoryRevision, "inventory full or money limit reached"));
            }

            candidate.RecordInventoryOperation(command);
            var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, field.FieldId, player.Position);
            var result = new PreparedPickupOperation(original, candidate, snapshot,
                new(true, candidate.InventoryRevision, string.Empty), field.GroundLoot, drop.Id, command.OperationId, drop.Gold ? drop.Amount : 0);
            prepared = true;
            return result;
        }
        finally
        {
            if (!prepared)
            {
                field.GroundLoot.Resolve(dropId, command.OperationId, committed: false);
            }
        }
    }
}
