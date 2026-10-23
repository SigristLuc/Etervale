using PristonTale.Application.Services;
using PristonTale.Application.World;

namespace PristonTale.Server.World;

// Stores the quest work the world tick queues (quest drops, quest weapon growth and expiry, leftovers) through the
// quest upkeep, a few players at a time, off the tick.
public sealed class QuestUpkeepService(WorldServer world, QuestAppService quests, ILogger<QuestUpkeepService> logger) : BackgroundService
{
    private const int Parallelism = 8;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var slots = new SemaphoreSlim(Parallelism);
        try
        {
            await foreach (var session in world.QuestUpkeepRequests.ReadAllAsync(stoppingToken))
            {
                await slots.WaitAsync(stoppingToken);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await quests.RunUpkeepAsync(session, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(exception, "Quest upkeep failed for {Character}", session.CharacterName);
                    }
                    finally
                    {
                        slots.Release();
                    }
                }, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        for (var slot = 0; slot < Parallelism; slot++)
        {
            await slots.WaitAsync(CancellationToken.None);
        }
    }
}
