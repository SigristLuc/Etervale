using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Quests;

namespace PristonTale.Application.Services;

// Quest requests and the quest upkeep outside the tick: the world prepares on the tick, the character is stored, then
// the world commits (DurableOperations). A request that changes nothing answers without a write.
public sealed class QuestAppService(ISessionRegistry sessions, ICharacterRepository characters, WorldServer world)
{
    // How long the upkeep waits for the save gate before trying again later.
    private static readonly TimeSpan UpkeepGateWait = TimeSpan.FromSeconds(2);

    private readonly DurableOperations _durable = new(characters);

    public async Task<QuestOutcome> RequestAsync(string sessionToken, QuestCommand command, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        return await lease.RunAsync(
            session => world.PrepareQuestAsync(session, command),
            world.CommitQuestAsync,
            prepared => prepared.Outcome,
            failure => failure switch
            {
                DurableFailure.NoSession => QuestOutcome.Failed(QuestStatus.Rejected, "invalid session"),
                DurableFailure.Busy => QuestOutcome.Failed(QuestStatus.Busy, "save or character operation pending; retry"),
                DurableFailure.StorageFailed => QuestOutcome.Failed(QuestStatus.StorageFailed, "storage rejected operation; character unchanged"),
                _ => QuestOutcome.Failed(QuestStatus.Rejected, "character is not playing")
            });
    }

    // Stores the quest work the tick queued for a player (quest drops, weapon growth, expired items). Returns false when
    // the work is still pending and must be tried again: a refused or failed write returns the drops for the next attempt.
    public async Task<bool> RunUpkeepAsync(PlayerSession session, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(session, UpkeepGateWait, ct);
        if (lease.Failure is not null)
        {
            await world.DeferQuestUpkeepAsync(session);
            return false;
        }

        return await lease.RunAsync(
            world.PrepareQuestUpkeepAsync,
            world.CommitQuestAsync,
            _ => true,
            failure => failure == DurableFailure.NotPlaying,
            abort: world.AbortQuestUpkeepAsync);
    }
}
