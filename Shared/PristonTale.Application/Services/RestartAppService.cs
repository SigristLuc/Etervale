using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Application.Services;

// The answer to a restart: where the player stands up (FieldId 0 when it did not).
public sealed record RestartReply(RestartOutcome Outcome, int FieldId = 0, System.Numerics.Vector3 Position = default);

// A dead player's restart: the tick prepares its costs and place, the character is stored, then the tick applies it. The
// session's SaveGate admits one durable operation at a time.
public sealed class RestartAppService(ISessionRegistry sessions, ICharacterRepository characters, WorldServer world)
{
    public async Task<RestartReply> RestartAsync(string sessionToken, RestartCommand command, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return new RestartReply(new RestartOutcome(RestartStatus.Rejected, command.Choice));
        }

        if (!await session.SaveGate.WaitAsync(0, ct))
        {
            return new RestartReply(new RestartOutcome(RestartStatus.Busy, command.Choice));
        }

        try
        {
            var prepared = await world.PrepareRestartAsync(session, command);
            if (prepared is null)
            {
                return new RestartReply(new RestartOutcome(RestartStatus.Rejected, command.Choice));
            }

            if (prepared.Snapshot is null)
            {
                return new RestartReply(prepared.Outcome);
            }

            // Finish an admitted restart despite cancellation.
            if (!await characters.UpdateAsync(prepared.Snapshot.Encode(), CancellationToken.None))
            {
                return new RestartReply(new RestartOutcome(RestartStatus.StorageFailed, command.Choice));
            }

            await world.CommitRestartAsync(session, prepared);
            return new RestartReply(prepared.Outcome, prepared.FieldId, prepared.Position);
        }
        finally
        {
            session.SaveGate.Release();
        }
    }
}
