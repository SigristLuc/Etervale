using PristonTale.Application.World;

namespace PristonTale.Server.World;

public sealed class WorldTickService : BackgroundService
{
    // Single-sourced on WorldServer so the offline tick driver runs the world at the same cadence.
    private const int TickHz = WorldServer.TickHz;

    private readonly WorldServer _world;
    private readonly ILogger<WorldTickService> _logger;

    public WorldTickService(WorldServer world, ILogger<WorldTickService> logger)
    {
        _world = world;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        const float dt = 1f / TickHz;
        uint tick = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(dt));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _world.Tick(dt, ++tick);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "World tick {Tick} failed", tick);
            }
        }
    }
}
