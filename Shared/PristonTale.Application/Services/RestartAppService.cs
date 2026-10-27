using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Contracts.World;

namespace PristonTale.Application.Services;

// The answer to a restart: where the player stands up (FieldId 0 when it did not).
public sealed record RestartReply(RestartOutcome Outcome, int FieldId = 0, System.Numerics.Vector3 Position = default);

// A dead player's restart: the tick prepares its costs and place, the character is stored, then the tick applies it
// (DurableOperations).
public sealed class RestartAppService(ISessionRegistry sessions, ICharacterRepository characters, WorldServer world)
{
    private readonly DurableOperations _durable = new(characters);

    public async Task<RestartReply> RestartAsync(string sessionToken, RestartCommand command, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        return await lease.RunAsync(
            session => world.PrepareRestartAsync(session, command),
            world.CommitRestartAsync,
            prepared => new RestartReply(prepared.Outcome, prepared.FieldId, prepared.Position),
            failure => new RestartReply(new RestartOutcome(failure switch
            {
                DurableFailure.Busy => RestartStatus.Busy,
                DurableFailure.StorageFailed => RestartStatus.StorageFailed,
                _ => RestartStatus.Rejected
            }, command.Choice)));
    }
}
