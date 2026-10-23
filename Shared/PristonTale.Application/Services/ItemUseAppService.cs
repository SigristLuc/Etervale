using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.Services;

// The answer to one item use; Arrival is set when the item moved the player to another field.
public sealed record ItemUseReply(ItemUseOutcome Outcome, TeleportArrival? Arrival = null);

// Item use from the inventory: the tick validates the use and prepares the consumed inventory with its effect, the
// character is stored, then the tick applies the effect. The session's SaveGate admits one durable operation at a time.
public sealed class ItemUseAppService(ISessionRegistry sessions, ICharacterRepository characters, WorldServer world)
{
    public async Task<ItemUseReply> UseAsync(string sessionToken, ItemUseCommand command, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return new ItemUseReply(ItemUseOutcome.Refused(command.ExpectedRevision, ItemUseStatus.Rejected));
        }

        if (!await session.SaveGate.WaitAsync(0, ct))
        {
            return new ItemUseReply(ItemUseOutcome.Refused(command.ExpectedRevision, ItemUseStatus.Busy));
        }

        try
        {
            var prepared = await world.PrepareItemUseAsync(session, command);
            if (prepared is null)
            {
                return new ItemUseReply(ItemUseOutcome.Refused(command.ExpectedRevision, ItemUseStatus.Rejected));
            }

            if (prepared.Snapshot is null)
            {
                return new ItemUseReply(prepared.Outcome);
            }

            // Finish an admitted transaction despite cancellation; retrying the operation id recovers the outcome.
            if (!await characters.UpdateAsync(prepared.Snapshot.Encode(), CancellationToken.None))
            {
                return new ItemUseReply(ItemUseOutcome.Refused(command.ExpectedRevision, ItemUseStatus.StorageFailed));
            }

            await world.CommitItemUseAsync(session, prepared);
            return new ItemUseReply(prepared.Outcome, prepared.Arrival);
        }
        finally
        {
            session.SaveGate.Release();
        }
    }
}
