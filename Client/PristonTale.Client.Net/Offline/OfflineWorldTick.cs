using PristonTale.Application.World;

namespace PristonTale.Client.Net.Offline;

internal sealed class OfflineWorldTick : IAsyncDisposable
{
    private readonly WorldServer _world;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public OfflineWorldTick(WorldServer world)
    {
        _world = world;
        _loop = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        const float dt = 1f / WorldServer.TickHz;
        uint tick = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(dt));

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    _world.Tick(dt, ++tick);
                }
                catch (Exception ex)
                {
                    // Keep the local world alive while exposing tick failures.
                    Console.Error.WriteLine($"[OfflineTick] tick {tick} FAILED: {ex}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }
        _cts.Dispose();
    }
}
