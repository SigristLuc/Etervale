using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.Services;

// The answer to one item use; Arrival is set when the item moved the player to another field.
public sealed record ItemUseReply(ItemUseOutcome Outcome, TeleportArrival? Arrival = null);

// Item use from the inventory: the tick validates the use and prepares the consumed inventory with its effect, the
// character is stored, then the tick applies the effect (DurableOperations).
public sealed class ItemUseAppService(ISessionRegistry sessions, ICharacterRepository characters, WorldServer world)
{
    private readonly DurableOperations _durable = new(characters);

    public async Task<ItemUseReply> UseAsync(string sessionToken, ItemUseCommand command, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        return await lease.RunAsync(
            session => world.PrepareItemUseAsync(session, command),
            world.CommitItemUseAsync,
            prepared => new ItemUseReply(prepared.Outcome, prepared.Arrival),
            failure => new ItemUseReply(ItemUseOutcome.Refused(command.ExpectedRevision, failure switch
            {
                DurableFailure.Busy => ItemUseStatus.Busy,
                DurableFailure.StorageFailed => ItemUseStatus.StorageFailed,
                _ => ItemUseStatus.Rejected
            })));
    }
}
