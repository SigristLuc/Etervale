using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// The shop a used mobile shop item opens anywhere: no NPC (entityId 0) and no stock, so it buys and repairs only, at the
// prices of a shop NPC. It stays open until the player closes it.
internal static class MobileShopTransactions
{
    public static bool Handles(NpcServiceCommand command) => command.NpcEntityId == 0 && command.Service == NpcServiceKind.Shop;

    public static PreparedNpcOperation Prepare(PlayerSession session, WorldPlayer player, FieldInstance field, NpcServiceCommand command)
    {
        var original = player.Character;
        if (!session.MobileShopOpen || player.IsDead)
        {
            return Final(original, NpcServiceOutcome.Failed(NpcServiceStatus.OutOfReach, "no shop is open here", original.InventoryRevision));
        }

        switch (command.Action)
        {
            case NpcServiceAction.Close:
                session.MobileShopOpen = false;
                return new PreparedNpcOperation(original, null, null, new NpcServiceReply(Done(original, "")));
            case NpcServiceAction.Open:
            case NpcServiceAction.Refresh:
                return new PreparedNpcOperation(original, null, null, new NpcServiceReply(Done(original, ""))
                {
                    Catalog = command.Action == NpcServiceAction.Open ? [] : null,
                    Quotes = NpcServiceTransactions.Quotes(original)
                });
            case NpcServiceAction.Sell:
            case NpcServiceAction.Repair:
            case NpcServiceAction.RepairAll:
                break;
            default:
                return Final(original, NpcServiceOutcome.Failed(NpcServiceStatus.Rejected, "this shop only buys and repairs", original.InventoryRevision));
        }

        if (original.ServiceOperations.Find(command.OperationId) is { } applied)
        {
            return applied.Command == command
                ? new PreparedNpcOperation(original, null, null,
                    new NpcServiceReply(Done(original, "already committed")) { Quotes = NpcServiceTransactions.Quotes(original) })
                : Final(original, NpcServiceOutcome.Failed(NpcServiceStatus.Conflict, "operation identity conflict", original.InventoryRevision));
        }

        if (command.OperationId == Guid.Empty || command.ExpectedRevision != original.InventoryRevision)
        {
            return Final(original, NpcServiceOutcome.Failed(NpcServiceStatus.Stale, ItemRuleMessages.StaleInventory, original.InventoryRevision));
        }

        var candidate = original.CloneForInventoryOperation();
        var status = command.Action switch
        {
            NpcServiceAction.Sell => NpcShopRules.TrySell(candidate, command.ItemId, MoneyLimits.CarryLimit(candidate.Level, candidate.Tier), out _),
            NpcServiceAction.Repair => NpcShopRules.TryRepair(candidate, command.ItemId, out _),
            _ => NpcShopRules.TryRepairAll(candidate, out _)
        };
        if (status != NpcServiceStatus.Ok)
        {
            var message = status == NpcServiceStatus.NotEnoughGold ? "not enough gold" : "the shop refused";
            return Final(original, NpcServiceOutcome.Failed(status, message, original.InventoryRevision));
        }

        candidate.RecordServiceOperation(command);
        var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, field.FieldId, player.Position);
        var reply = new NpcServiceReply(Done(candidate, "")) { Quotes = NpcServiceTransactions.Quotes(candidate) };
        return new PreparedNpcOperation(original, candidate, snapshot, reply, candidate.Money - original.Money);
    }

    private static NpcServiceOutcome Done(PlayerCharacter character, string message) =>
        new(true, NpcServiceStatus.Ok, character.InventoryRevision, 0, message);

    private static PreparedNpcOperation Final(PlayerCharacter original, NpcServiceOutcome outcome) =>
        new(original, null, null, new NpcServiceReply(outcome));
}
