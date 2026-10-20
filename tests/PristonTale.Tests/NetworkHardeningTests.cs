using System.Net;
using System.Numerics;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PristonTale.Application.World;
using PristonTale.Server.Hosting;
using PristonTale.Server.Net;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

internal static class NetworkHardeningTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("packet flood limit follows the original 251 packets per 5 s and resets per window", PacketLimitAsync);
        yield return ("player info bucket, per-address connections and sampled logs bound abusive clients", BucketsAsync);
        yield return ("login throttle blocks repeated failures per account and per address", LoginThrottleAsync);
        yield return ("network limits read configuration and reject negative values", NetworkConfigurationAsync);
        yield return ("world command quota and a full queue refuse synchronously and per player", CommandQuotaAsync);
        yield return ("world options validate the experience multiplier", WorldOptionsAsync);
        yield return ("server startup fails without field terrain unless explicitly allowed", TerrainStartupAsync);
    }

    private static Task PacketLimitAsync()
    {
        var limiter = new PacketRateLimiter(251, 5000);
        for (var packet = 0; packet < 251; packet++)
        {
            Assert.True(limiter.Admit(1000 + packet), "a packet within the original limit was refused");
        }

        Assert.True(!limiter.Admit(4000), "packet 252 inside five seconds was admitted");
        Assert.True(limiter.Admit(6000), "a new window did not reset the count");
        Assert.True(new PacketRateLimiter(0, 5000).Admit(0), "a disabled limit refused a packet");
        return Task.CompletedTask;
    }

    private static Task BucketsAsync()
    {
        var bucket = new TokenBucket(3, 2);
        Assert.True(bucket.TryTake(0) && bucket.TryTake(0) && bucket.TryTake(0), "burst capacity refused");
        Assert.True(!bucket.TryTake(0), "bucket exceeded its burst");
        Assert.True(!bucket.TryTake(400) && bucket.TryTake(600), "bucket refilled at the wrong rate");

        var connections = new ConnectionLimiter(2);
        var address = IPAddress.Parse("10.0.0.1");
        Assert.True(connections.TryAcquire(address) && connections.TryAcquire(address), "connections under the limit refused");
        Assert.True(!connections.TryAcquire(address), "a third connection from one address was accepted");
        Assert.True(connections.TryAcquire(IPAddress.Parse("10.0.0.2")), "another address shared the limit");
        connections.Release(address);
        Assert.True(connections.TryAcquire(address), "a closed connection did not free its slot");
        Assert.True(NetworkLimits.AddressOf(new IPEndPoint(address.MapToIPv6(), 1))!.Equals(address), "IPv4-mapped peers counted separately");

        var sampler = new LogSampler(1000);
        Assert.True(sampler.TryEnter(0, out var none) && none == 0, "first rejection not logged");
        Assert.True(!sampler.TryEnter(10, out _) && !sampler.TryEnter(20, out _), "rejections inside the interval were logged");
        Assert.True(sampler.TryEnter(1000, out var suppressed) && suppressed == 2, "suppressed rejections were not counted");
        return Task.CompletedTask;
    }

    private static Task LoginThrottleAsync()
    {
        var throttle = new LoginThrottle(3, 5, 60_000);
        var address = IPAddress.Parse("10.0.0.7");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.True(!throttle.IsBlocked(address, "Victim", attempt), "account blocked before its failure limit");
            throttle.RecordFailure(address, "Victim", attempt);
        }

        Assert.True(throttle.IsBlocked(IPAddress.Parse("10.0.0.8"), "victim", 10), "account throttle ignored another address or case");
        Assert.True(!throttle.IsBlocked(IPAddress.Parse("10.0.0.8"), "other", 10), "unrelated account and address blocked");
        throttle.RecordFailure(address, "other", 11);
        throttle.RecordFailure(address, "third", 12);
        Assert.True(throttle.IsBlocked(address, "fresh", 13), "address throttle ignored five failures");
        Assert.True(!throttle.IsBlocked(address, "fresh", 60_013), "throttle outlived its window");
        throttle.RecordSuccess("other");
        throttle.Prune(120_000);
        Assert.True(!throttle.IsBlocked(address, "Victim", 120_000), "pruned windows still blocked");
        return Task.CompletedTask;
    }

    private static Task NetworkConfigurationAsync()
    {
        var defaults = NetworkLimits.From(Configuration());
        Assert.True(defaults.PacketLimit == 251 && defaults.PacketWindowMs == 5000, "packet limit default left the original values");
        var custom = NetworkLimits.From(Configuration(("Network:PacketLimit", "0"), ("Network:LoginTimeoutSeconds", "12"), ("Network:MaxConnectionsPerIp", "0")));
        Assert.True(custom.PacketLimit == 0 && custom.LoginTimeoutMs == 12_000 && custom.MaxConnectionsPerIp == 0, "network configuration ignored");
        var threw = false;
        try
        {
            NetworkLimits.From(Configuration(("Network:PacketLimit", "-1")));
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Assert.True(threw, "a negative network limit was accepted");
        return Task.CompletedTask;
    }

    private static async Task CommandQuotaAsync()
    {
        var catalog = new WorldCatalog([new FieldDef("fixture", "Fixture", FieldState.Forest, new FieldBackdrop("", "", ""), "", []) { FieldCode = 3 }]);
        var world = new WorldServer(new WorldDataHolder(), catalog, options: new WorldServerOptions { MaxPendingCommandsPerPlayer = 4 });
        var player = new WorldPlayer(7, new Vector3(100, 0, 100), new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Quota"), false, default);
        var join = world.JoinAsync(3, player, Channel.CreateUnbounded<WorldSnapshot>().Writer);
        world.Tick(0.05f, 1);
        Assert.True(await join, "fixture join failed");

        var accepted = new List<Task<MovementVerdict>>();
        for (var report = 0; report < 4; report++)
        {
            accepted.Add(world.ReportMovementAsync(3, player.EntityId, new Vector3(101 + report, 0, 100), 0f, true, false, 0));
        }

        Assert.True(Throws(() => world.ReportMovementAsync(3, player.EntityId, new Vector3(110, 0, 100), 0f, true, false, 0)),
            "a fifth pending command passed the player's quota");
        Assert.True(world.PendingCommandsOf(player.EntityId) == 4, "a refused command stayed counted");
        var other = world.ReportMovementAsync(3, 99, Vector3.Zero, 0f, false, false, 0);
        world.Tick(0.05f, 2);
        await Task.WhenAll(accepted);
        Assert.True(await other == MovementVerdict.Ignored, "another player's command was refused by this player's quota");
        Assert.True(world.PendingCommandsOf(player.EntityId) == 0, "executed commands did not release the quota");
        accepted.Add(world.ReportMovementAsync(3, player.EntityId, new Vector3(110, 0, 100), 0f, true, false, 0));
        world.Tick(0.05f, 3);
        await Task.WhenAll(accepted);

        var full = new WorldServer(new WorldDataHolder(), catalog);
        var refused = false;
        for (uint owner = 1; owner <= 2000 && !refused; owner++)
        {
            for (var command = 0; command < 40 && !refused; command++)
            {
                refused = Throws(() => full.ReportMovementAsync(3, owner, Vector3.Zero, 0f, false, false, 0));
            }
        }

        Assert.True(refused, "a full command queue did not refuse synchronously");
        var leave = full.LeaveAsync(3, 1, Channel.CreateUnbounded<WorldSnapshot>().Writer);
        for (uint tick = 0; tick < 20 && !leave.IsCompleted; tick++)
        {
            full.Tick(0.05f, tick);
            await Task.Delay(1);
        }

        Assert.True(leave.IsCompletedSuccessfully, "leaving the world waited forever behind a full queue");
    }

    private static Task WorldOptionsAsync()
    {
        Assert.True(WorldServerOptions.Default.ExperienceMultiplier == 5, "default experience multiplier changed");
        Assert.True(Throws(() => new WorldServerOptions { ExperienceMultiplier = 0 }.Validated()), "an experience multiplier of 0 was accepted");
        Assert.True(Throws(() => new WorldServerOptions { MaxPendingCommandsPerPlayer = 0 }.Validated()), "a zero command quota was accepted");
        return Task.CompletedTask;
    }

    private static async Task TerrainStartupAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"terrain-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var world = new WorldCatalog([new FieldDef("fixture", "Fixture", FieldState.Forest, new FieldBackdrop("", "", ""), "Field/missing.smd", [])
            {
                FieldCode = 3
            }]);
            var strict = new FieldTerrainLoadService(world, new FieldTerrainCatalog(), Configuration(("Assets:Root", root)),
                NullLogger<FieldTerrainLoadService>.Instance);
            var failed = false;
            try
            {
                await strict.StartAsync(CancellationToken.None);
            }
            catch (InvalidOperationException ex)
            {
                failed = ex.Message.Contains("missing.smd", StringComparison.Ordinal);
            }

            Assert.True(failed, "a missing stage did not stop startup");
            var missingRoot = new FieldTerrainLoadService(world, new FieldTerrainCatalog(), Configuration(("Assets:Root", Path.Combine(root, "absent"))),
                NullLogger<FieldTerrainLoadService>.Instance);
            Assert.True(await ThrowsAsync(() => missingRoot.StartAsync(CancellationToken.None)), "a missing assets directory did not stop startup");
            var lenient = new FieldTerrainLoadService(world, new FieldTerrainCatalog(),
                Configuration(("Assets:Root", root), ("Assets:AllowMissingTerrain", "true")), NullLogger<FieldTerrainLoadService>.Instance);
            await lenient.StartAsync(CancellationToken.None);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value))).Build();
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static async Task<bool> ThrowsAsync(Func<Task> action)
    {
        try
        {
            await action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
