using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.World;

namespace PristonTale.Application.Services;

// The answer to a wing gate trip; Arrival is where the player now stands when it warped.
public sealed record WingGateReply(WingGateOutcome Outcome, TeleportArrival? Arrival = null);

// A trip through the wing warp gates: the tick prepares the fee and the place, the character is stored, then the tick
// applies it (DurableOperations).
public sealed class WingGateAppService(ISessionRegistry sessions, ICharacterRepository characters, WorldServer world)
{
    private readonly DurableOperations _durable = new(characters);

    public async Task<WingGateReply> TravelAsync(string sessionToken, WingGateCommand command, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        return await lease.RunAsync(
            session => world.PrepareWingGateAsync(session, command),
            world.CommitWingGateAsync,
            prepared => new WingGateReply(prepared.Outcome, prepared.Arrival),
            failure => new WingGateReply(new WingGateOutcome(failure switch
            {
                DurableFailure.Busy => WingGateStatus.Busy,
                DurableFailure.StorageFailed => WingGateStatus.StorageFailed,
                _ => WingGateStatus.Rejected
            }, command.Destination)));
    }
}
