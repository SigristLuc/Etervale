using System.Diagnostics;
using PristonTale.Application.World;

namespace PristonTale.Server.World;

public sealed class WorldTickService : BackgroundService
{
    private const int TickHz = WorldServer.TickHz;
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(30);

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
        var durations = new List<double>(TickHz * (int)ReportInterval.TotalSeconds);
        var lastReport = Stopwatch.GetTimestamp();

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                _world.Tick(dt, ++tick);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "World tick {Tick} failed", tick);
            }

            durations.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (Stopwatch.GetElapsedTime(lastReport) >= ReportInterval)
            {
                durations.Sort();
                _logger.LogInformation("World tick: {Players} players, p50 {P50:F2} ms, p99 {P99:F2} ms, max {Max:F2} ms over {Count} ticks (budget {Budget:F0} ms)",
                    _world.PlayerCount, durations[durations.Count / 2], durations[(int)(durations.Count * 0.99)], durations[^1], durations.Count, 1000.0 / TickHz);
                durations.Clear();
                lastReport = Stopwatch.GetTimestamp();
            }
        }
    }
}
