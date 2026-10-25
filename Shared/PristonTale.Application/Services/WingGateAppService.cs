using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.Services;

// The answer to a wing gate trip; Arrival is where the player now stands when it warped.
public sealed record WingGateReply(WingGateOutcome Outcome, TeleportArrival? Arrival = null);

// A trip through the wing warp gates: the tick prepares the fee and the place, the character is stored, then the tick
// applies it. The session's SaveGate admits one durable operation at a time.
public sealed class WingGateAppService(ISessionRegistry sessions, ICharacterRepository characters, WorldServer world)
{
    public async Task<WingGateReply> TravelAsync(string sessionToken, WingGateCommand command, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return new WingGateReply(new WingGateOutcome(WingGateStatus.Rejected, command.Destination));
        }

        if (!await session.SaveGate.WaitAsync(0, ct))
        {
            return new WingGateReply(new WingGateOutcome(WingGateStatus.Busy, command.Destination));
        }

        try
        {
            var prepared = await world.PrepareWingGateAsync(session, command);
            if (prepared is null)
            {
                return new WingGateReply(new WingGateOutcome(WingGateStatus.Rejected, command.Destination));
            }

            if (prepared.Snapshot is null)
            {
                return new WingGateReply(prepared.Outcome);
            }

            // Finish an admitted trip despite cancellation.
            if (!await characters.UpdateAsync(prepared.Snapshot.Encode(), CancellationToken.None))
            {
                return new WingGateReply(new WingGateOutcome(WingGateStatus.StorageFailed, command.Destination));
            }

            await world.CommitWingGateAsync(session, prepared);
            return new WingGateReply(prepared.Outcome, prepared.Arrival);
        }
        finally
        {
            session.SaveGate.Release();
        }
    }
}
