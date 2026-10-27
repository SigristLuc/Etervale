using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Quests;

namespace PristonTale.Application.Services;

// Quest requests and the quest upkeep outside the tick: the world prepares on the tick, the character is stored here
// under the save gate, then the world commits. A request that changes nothing answers without a write.
public sealed class QuestAppService(ISessionRegistry sessions, ICharacterRepository characters, WorldServer world)
{
    // How long the upkeep waits for the save gate before trying again later.
    private static readonly TimeSpan UpkeepGateWait = TimeSpan.FromSeconds(2);

    public async Task<QuestOutcome> RequestAsync(string sessionToken, QuestCommand command, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return QuestOutcome.Failed(QuestStatus.Rejected, "invalid session");
        }

        if (!await session.SaveGate.WaitAsync(0, ct))
        {
            return QuestOutcome.Failed(QuestStatus.Busy, "save or character operation pending; retry");
        }

        try
        {
            var prepared = await world.PrepareQuestAsync(session, command);
            if (prepared is null)
            {
                return QuestOutcome.Failed(QuestStatus.Rejected, "character is not playing");
            }

            if (prepared.Snapshot is null)
            {
                return prepared.Outcome;
            }

            // Finish an admitted transaction despite cancellation. Retrying its operation id recovers the outcome.
            if (!await characters.UpdateAsync(prepared.Snapshot.Encode(), CancellationToken.None))
            {
                return QuestOutcome.Failed(QuestStatus.StorageFailed, "storage rejected operation; character unchanged");
            }

            await world.CommitQuestAsync(session, prepared);
            return prepared.Outcome;
        }
        finally
        {
            session.SaveGate.Release();
        }
    }

    // Stores the quest work the tick queued for a player (quest drops, weapon growth, expired items). Returns false when
    // the work is still pending and must be tried again.
    public async Task<bool> RunUpkeepAsync(PlayerSession session, CancellationToken ct = default)
    {
        if (!await session.SaveGate.WaitAsync(UpkeepGateWait, ct))
        {
            await world.DeferQuestUpkeepAsync(session);
            return false;
        }

        try
        {
            var prepared = await world.PrepareQuestUpkeepAsync(session);
            if (prepared?.Snapshot is null)
            {
                return true;
            }

            // A refused or failed write (a storage exception propagates) returns the drops for the next attempt.
            var stored = false;
            try
            {
                stored = await characters.UpdateAsync(prepared.Snapshot.Encode(), CancellationToken.None);
            }
            finally
            {
                if (!stored)
                {
                    await world.AbortQuestUpkeepAsync(session, prepared);
                }
            }

            if (!stored)
            {
                return false;
            }

            await world.CommitQuestAsync(session, prepared);
            return true;
        }
        finally
        {
            session.SaveGate.Release();
        }
    }
}
