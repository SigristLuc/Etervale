using System.Diagnostics;
using PristonTale.Application.Chat;
using PristonTale.Application.World;

namespace PristonTale.Server.World;

public sealed class WorldTickService : BackgroundService
{
    private const int TickHz = WorldServer.TickHz;
    public const int DefaultReportSeconds = 30;

    private readonly WorldServer _world;
    private readonly ChatDirectory _chat;
    private readonly ILogger<WorldTickService> _logger;
    private readonly TimeSpan _reportInterval;

    public WorldTickService(WorldServer world, ChatDirectory chat, ILogger<WorldTickService> logger, IConfiguration configuration)
    {
        _world = world;
        _chat = chat;
        _logger = logger;
        var seconds = configuration.GetValue("Server:ReportSeconds", DefaultReportSeconds);
        if (seconds < 1)
        {
            throw new InvalidOperationException("Server:ReportSeconds must be at least 1.");
        }

        _reportInterval = TimeSpan.FromSeconds(seconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        const float dt = 1f / TickHz;
        uint tick = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(dt));
        var durations = new List<double>(TickHz * (int)_reportInterval.TotalSeconds);
        var lastReport = Stopwatch.GetTimestamp();
        var process = Process.GetCurrentProcess();
        var lastCpu = process.TotalProcessorTime;
        var lastAllocated = GC.GetTotalAllocatedBytes(precise: true);
        var lastCollections = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                _world.Tick(dt, ++tick);
                _chat.Deliver(_world.TakeTickNotices());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "World tick {Tick} failed", tick);
            }

            durations.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            var elapsed = Stopwatch.GetElapsedTime(lastReport);
            if (elapsed >= _reportInterval)
            {
                durations.Sort();
                process.Refresh();
                var cpu = process.TotalProcessorTime;
                var allocated = GC.GetTotalAllocatedBytes(precise: true);
                var collections = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
                _logger.LogInformation(
                    "World tick: {Players} players, p50 {P50:F2} ms, p99 {P99:F2} ms, max {Max:F2} ms over {Count} ticks (budget {Budget:F0} ms); " +
                    "process {Cores:F2} cores, {Allocated:F1} MiB/s allocated, GC {Gen0}/{Gen1}/{Gen2}, heap {Heap:F0} MiB, working set {WorkingSet:F0} MiB",
                    _world.PlayerCount, durations[durations.Count / 2], durations[(int)(durations.Count * 0.99)], durations[^1], durations.Count, 1000.0 / TickHz,
                    (cpu - lastCpu).TotalSeconds / elapsed.TotalSeconds, (allocated - lastAllocated) / 1048576.0 / elapsed.TotalSeconds,
                    collections.Item1 - lastCollections.Item1, collections.Item2 - lastCollections.Item2, collections.Item3 - lastCollections.Item3,
                    GC.GetTotalMemory(false) / 1048576.0, process.WorkingSet64 / 1048576.0);
                durations.Clear();
                lastReport = Stopwatch.GetTimestamp();
                lastCpu = cpu;
                lastAllocated = allocated;
                lastCollections = collections;
            }
        }
    }
}
