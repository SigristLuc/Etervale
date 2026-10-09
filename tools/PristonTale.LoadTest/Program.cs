using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using PristonTale.Client.Net;
using PristonTale.Simulation.Characters;

// Headless load generator over the original Priston protocol. Each bot logs in, creates a character when the account
// has none, enters the world and walks a circle while reporting TRANSPLAYDATA at 5 Hz and draining presence.
// Accounts come from: dotnet run --project tools/PristonTale.DevAccounts -- create-bots <prefix> <count> <password>
var options = Options.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("usage: --count <bots> [--address tcp://localhost:10009] [--prefix bot] [--password bot-pw] [--seconds 60] [--ramp 50]");
    return 1;
}

Console.WriteLine($"{options.Count} bots against {options.Address}, ramp {options.RampPerSecond}/s, {options.Seconds} s in world");
var stats = new Stats();
using var stop = new CancellationTokenSource();
var bots = new List<Task>(options.Count);
var ramp = Stopwatch.StartNew();
for (var index = 1; index <= options.Count; index++)
{
    bots.Add(RunBotAsync(index, options, stats, stop.Token));
    var due = TimeSpan.FromSeconds(index / (double)options.RampPerSecond);
    if (due > ramp.Elapsed)
    {
        await Task.Delay(due - ramp.Elapsed);
    }
}

var measured = Stopwatch.StartNew();
long lastBytes = 0;
long lastUpdates = 0;
while (measured.Elapsed < TimeSpan.FromSeconds(options.Seconds))
{
    await Task.Delay(TimeSpan.FromSeconds(5));
    var bytes = Interlocked.Read(ref stats.BytesReceived);
    var updates = Interlocked.Read(ref stats.Updates);
    var playing = Volatile.Read(ref stats.Playing);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"t={measured.Elapsed.TotalSeconds,5:F0}s playing={playing,5} failed={Volatile.Read(ref stats.Failures),4} " +
        $"in={(bytes - lastBytes) / 1024.0 / 5,9:F1} KiB/s ({(playing == 0 ? 0 : (bytes - lastBytes) / 1024.0 / 5 / playing),6:F2} per bot) " +
        $"updates/s={(updates - lastUpdates) / 5.0,8:F0} corrections={Volatile.Read(ref stats.Corrections)}"));
    lastBytes = bytes;
    lastUpdates = updates;
}

await stop.CancelAsync();
await Task.WhenAll(bots);
Console.WriteLine($"done: {stats.Entered} entered, {stats.Failures} failed, {stats.Corrections} position corrections, {stats.LeaveTimeouts} leave timeouts");
return stats.Failures == 0 ? 0 : 2;

static async Task RunBotAsync(int index, Options options, Stats stats, CancellationToken stop)
{
    var account = $"{options.Prefix}{index:D4}";
    try
    {
        await using var connection = new GameServerConnection(options.Address);
        var login = await connection.LoginAsync(account, options.Password, GameServerConnection.EnglishClientVersion, stop);
        if (!login.Ok)
        {
            throw new InvalidOperationException($"login: {login.Message}");
        }

        var characters = await connection.ListCharactersAsync(stop);
        var name = characters.FirstOrDefault()?.Name;
        if (name is null)
        {
            name = $"Lt{options.Prefix}{index:D4}";
            var created = await connection.CreateCharacterAsync(name, (int)PlayerClass.Fighter, 0, stop);
            if (!created.Ok)
            {
                throw new InvalidOperationException($"create: {created.Message}");
            }
        }

        var enter = await connection.EnterWorldAsync(name, stop);
        if (!enter.Ok)
        {
            throw new InvalidOperationException($"enter: {enter.Message}");
        }

        Interlocked.Increment(ref stats.Entered);
        var play = connection.OpenPlayChannel(stop);
        Interlocked.Increment(ref stats.Playing);
        var corrected = new StrongBox<Vector2?>(null);
        var reader = DrainAsync(play, stats, corrected, stop);
        const float Radius = 150f;
        var phase = index * 0.37f;
        // The circle passes through the spawn so the first report stays within one step of the server position.
        var center = new Vector2(enter.SpawnX, enter.SpawnZ) - new Vector2(MathF.Cos(phase), MathF.Sin(phase)) * Radius;
        const float UnitsPerSecond = 80f;
        long lastBytes = 0;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            while (await timer.WaitForNextTickAsync(stop))
            {
                Vector2? warp;
                lock (corrected)
                {
                    warp = corrected.Value;
                    corrected.Value = null;
                }

                if (warp is { } authoritative)
                {
                    // A forced WARPFIELD moves the bot; restart its circle from the authoritative position.
                    center = authoritative - new Vector2(MathF.Cos(phase), MathF.Sin(phase)) * Radius;
                }

                phase += UnitsPerSecond * 0.2f / Radius;
                var position = new Vector3(center.X + MathF.Cos(phase) * Radius, 0f, center.Y + MathF.Sin(phase) * Radius);
                var facing = MathF.Atan2(-MathF.Sin(phase), MathF.Cos(phase));
                await play.SendMovementAsync(position, facing, true, false, 0, enter.FieldId, stop);
                var bytes = connection.BytesReceived;
                Interlocked.Add(ref stats.BytesReceived, bytes - lastBytes);
                lastBytes = bytes;
            }
        }
        catch (OperationCanceledException)
        {
        }

        Interlocked.Decrement(ref stats.Playing);
        try
        {
            // Leaving waits for the server-side save, which queues behind every other bot leaving at once.
            await play.DisposeAsync();
        }
        catch (TimeoutException)
        {
            Interlocked.Increment(ref stats.LeaveTimeouts);
        }

        await reader;
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested)
    {
    }
    catch (Exception ex)
    {
        if (Interlocked.Increment(ref stats.Failures) <= 10)
        {
            Console.Error.WriteLine($"{account}: {ex.GetBaseException().Message}");
        }
    }
}

static async Task DrainAsync(IPlayChannel play, Stats stats, StrongBox<Vector2?> corrected, CancellationToken stop)
{
    try
    {
        await foreach (var update in play.ReadAllAsync(stop))
        {
            Interlocked.Increment(ref stats.Updates);
            if (update.Warp is { Forced: true } warp)
            {
                Interlocked.Increment(ref stats.Corrections);
                lock (corrected)
                {
                    corrected.Value = new Vector2(warp.Position.X, warp.Position.Z);
                }
            }
        }
    }
    catch (Exception ex) when (ex is OperationCanceledException or IOException)
    {
    }
}

internal sealed class Stats
{
    public long BytesReceived;
    public long Updates;
    public int Playing;
    public int Entered;
    public int Failures;
    public int Corrections;
    public int LeaveTimeouts;
}

internal sealed record Options(string Address, string Prefix, string Password, int Count, int Seconds, int RampPerSecond)
{
    public static Options? Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index + 1 < args.Length; index += 2)
        {
            values[args[index]] = args[index + 1];
        }

        if (!values.TryGetValue("--count", out var countText) || !int.TryParse(countText, out var count) || count < 1)
        {
            return null;
        }

        return new Options(
            values.GetValueOrDefault("--address", "tcp://localhost:10009"),
            values.GetValueOrDefault("--prefix", "bot"),
            values.GetValueOrDefault("--password", "bot-pw"),
            count,
            int.Parse(values.GetValueOrDefault("--seconds", "60"), CultureInfo.InvariantCulture),
            Math.Max(1, int.Parse(values.GetValueOrDefault("--ramp", "50"), CultureInfo.InvariantCulture)));
    }
}
