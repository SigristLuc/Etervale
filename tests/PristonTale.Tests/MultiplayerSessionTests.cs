using System.Numerics;
using PristonTale.Application.Services;
using PristonTale.Client.Net.Presence;
using static PristonTale.Tests.MultiplayerMovementTests;
using PristonTale.Contracts.World;

namespace PristonTale.Tests;

// Session lifecycle seen by other players: leaving, losing the connection, coming back, and two logins racing for one
// character.
internal static class MultiplayerSessionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("multiplayer: leaving the world removes the player for its viewers and a reconnect shows it again where it left", LeaveAsync);
        yield return ("multiplayer: a dropped connection removes the player and frees its character for the next login", DropAsync);
        yield return ("multiplayer: two logins racing for one character admit exactly one and viewers see one avatar", RaceAsync);
    }

    private static async Task LeaveAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var camp = MultiplayerServer.Camp;
        var account = await server.CreatePlayerAsync(new PlayerSetup("Returner", Simulation.Characters.PlayerClass.Fighter, 10, MultiplayerServer.HuntingField, camp));
        var a = await MultiplayerClient.EnterAsync(server, account, "Returner");
        await using var b = await Enter(server, "Stayer", camp + new Vector3(150f, 0f, 0f));
        await b.WaitUntilAsync(world => world.Entities.ContainsKey(a.Serial), "the returner");
        var moved = camp + new Vector3(0f, 0f, 96f);
        await a.WalkAsync(camp, moved);
        await b.WaitUntilAsync(world => Vector3.Distance(Planar(world.Entities[a.Serial].Position), Planar(moved)) < 0.05f, "the returner's walk");

        var first = a.Serial;
        await a.LeaveAsync();
        await b.WaitUntilAsync(world => world.Removed.Contains(first) && !world.Entities.ContainsKey(first), "the returner leaving");

        await using var again = await MultiplayerClient.EnterAsync(server, account, "Returner");
        var back = await b.WaitAsync(world => world.Entities.GetValueOrDefault(again.Serial), "the returner back");
        Assert.True(back.Player?.Name == "Returner" && Vector3.Distance(Planar(back.Position), Planar(moved)) < 1f, "the returner came back elsewhere");
        Assert.True(b.World.Read(world => world.Entities.Values.Count(entity => entity.Player?.Name == "Returner")) == 1, "the viewer shows two returners");
    }

    private static async Task DropAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var camp = MultiplayerServer.Camp;
        var account = await server.CreatePlayerAsync(new PlayerSetup("Flaky", Simulation.Characters.PlayerClass.Fighter, 10, MultiplayerServer.HuntingField, camp));
        var a = await MultiplayerClient.EnterAsync(server, account, "Flaky");
        await using var b = await Enter(server, "Patient", camp + new Vector3(150f, 0f, 0f));
        await b.WaitUntilAsync(world => world.Entities.ContainsKey(a.Serial), "the flaky player");

        await a.DropAsync();
        await b.WaitUntilAsync(world => world.Removed.Contains(a.Serial), "the dropped player removed");
        // The character is claimed until the server has saved it; the next login may have to retry briefly.
        await using var again = await MultiplayerClient.LoginAsync(server, account, "Flaky");
        EnterWorldOutcome? entered = null;
        for (var attempt = 0; attempt < 50 && entered is not { Ok: true }; attempt++)
        {
            entered = await again.TryEnterAsync();
            if (!entered.Ok)
            {
                Assert.True(entered.Failure == EnterWorldFailure.AlreadyActive, $"re-entry failed with {entered.Failure}");
                await Task.Delay(100);
            }
        }

        Assert.True(entered is { Ok: true }, "the character stayed claimed after the connection dropped");
        again.Adopt(entered!);
        await b.WaitUntilAsync(world => world.Entities.TryGetValue(again.Serial, out var value) && value.Player?.Name == "Flaky", "the player back");
    }

    private static async Task RaceAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var camp = MultiplayerServer.Camp;
        var account = await server.CreatePlayerAsync(new PlayerSetup("Contested", Simulation.Characters.PlayerClass.Fighter, 10, MultiplayerServer.HuntingField, camp));
        await using var b = await Enter(server, "Referee", camp + new Vector3(150f, 0f, 0f));
        await using var first = await MultiplayerClient.LoginAsync(server, account, "Contested");
        await using var second = await MultiplayerClient.LoginAsync(server, account, "Contested");
        var outcomes = await Task.WhenAll(first.TryEnterAsync(), second.TryEnterAsync());
        Assert.True(outcomes.Count(outcome => outcome.Ok) == 1, $"{outcomes.Count(outcome => outcome.Ok)} sessions entered one character");
        Assert.True(outcomes.Single(outcome => !outcome.Ok).Failure == EnterWorldFailure.AlreadyActive, "the loser was refused for another reason");
        var winner = outcomes[0].Ok ? first : second;
        winner.Adopt(outcomes.Single(outcome => outcome.Ok));
        await b.WaitUntilAsync(world => world.Entities.ContainsKey(winner.Serial), "the winner");
        await Task.Delay(300);
        Assert.True(b.World.Read(world => world.Entities.Values.Count(entity => entity.Player?.Name == "Contested")) == 1, "the viewer shows two avatars");
        Assert.True(b.World.Read(world => world.Entities[winner.Serial].Kind) == EntityKind.Player, "the winner is not a player");
    }
}
