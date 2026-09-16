using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Gameplay.World;
using PristonTale.Gameplay.World.Streaming;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Fields;

namespace PristonTale.Tests;

internal static class MovementAndStreamingTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("prediction and authority stop at the same point at different tick rates", ArrivalAsync);
        yield return ("blocked final movement remains pending until collision accepts it", BlockedArrivalAsync);
        yield return ("streaming follows the movement destination without loading a distant adjacent field while idle", PrefetchAsync);
        yield return ("NPC approach stops locally before authority confirms interaction range", NpcApproachAsync);
        yield return ("continuous gates preserve a running destination while warps cancel it", GateMovementAsync);
    }

    private static Task ArrivalAsync()
    {
        foreach (var offset in new[] { new Vector2(1, 0), new Vector2(5, -5), new Vector2(-11, 0), new Vector2(300, 170) })
        {
            foreach (var running in new[] { false, true })
            {
                var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Arrival");
                var start = new Vector3(100, 0, 100);
                var target = new Vector2(start.X, start.Z) + offset;
                var player = new WorldPlayer(1, start, character, true, default);
                player.SetTarget(new Vector3(target.X, 0, target.Y), running);
                var predicted = new Vector2(start.X, start.Z);
                var speed = running
                    ? Locomotion.RunUnitsPerSecond(character.Derived.MoveSpeed)
                    : Locomotion.WalkUnitsPerSecond(character.Derived.MoveSpeed, true);
                for (var frame = 0; frame < 600; frame++)
                {
                    predicted = DestinationMovement.Advance(predicted, target, speed, 1f / 60f).Position;
                    if (frame % 3 == 0)
                    {
                        player.CommitStep(player.IntegrateStep(0.05f));
                    }
                }

                Assert.True(predicted == target, "prediction stopped before the clicked point");
                Assert.True(new Vector2(player.Position.X, player.Position.Z) == target, "authority stopped at another point");
                Assert.True(player.MoveState == MoveStateView.Idle, "arrival did not publish idle");
                for (var tick = 0; tick < 20; tick++)
                {
                    Assert.True(player.IntegrateStep(0.05f) == player.Position, "idle moved the avatar after arrival");
                }
            }
        }

        var unchanged = DestinationMovement.Advance(Vector2.One, new Vector2(20), 100, float.NaN);
        Assert.True(unchanged.Position == Vector2.One && !unchanged.Arrived, "invalid delta time moved prediction");
        return Task.CompletedTask;
    }

    private static Task BlockedArrivalAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Blocked");
        var player = new WorldPlayer(1, new Vector3(100, 0, 100), character, true, default);
        var target = player.Position + Vector3.UnitX;
        player.SetTarget(target, false);
        Assert.True(player.IntegrateStep(0.05f) == target, "short destination was not proposed");
        player.CommitStep(player.Position);
        Assert.True(player.MoveState == MoveStateView.Walking, "rejected destination was marked idle");
        Assert.True(player.IntegrateStep(0.05f) == target, "collision rejection lost the destination");
        player.CommitStep(target);
        Assert.True(player.MoveState == MoveStateView.Idle && player.IntegrateStep(0.05f) == target, "accepted arrival did not stop");
        return Task.CompletedTask;
    }

    private static Task PrefetchAsync()
    {
        var forest = Forest1Field.Definition;
        var spawn = forest.StartPoints[0];
        var gate = forest.Gates.Single(value => value.DestinationFieldId == "village-2");
        var player = new Vector2(spawn.X, spawn.Z);
        var portal = new Vector2(gate.X, gate.Z);
        var distanceSquared = Vector2.DistanceSquared(player, portal);
        Assert.True(distanceSquared > FieldStreamingPolicy.IdlePrefetchDistance * FieldStreamingPolicy.IdlePrefetchDistance,
            "Forest 1 fixture no longer distinguishes idle and approach prefetch");
        Assert.True(!FieldStreamingPolicy.ShouldPrefetch(player, null, portal, 0f),
            "stationary Forest 1 spawn loaded Ricarten");
        var walking = FieldStreamingPolicy.PrefetchDistance(100f);
        var sprinting = FieldStreamingPolicy.PrefetchDistance(300f);
        var towardPortal = Vector2.Lerp(player, portal, 0.5f);
        var awayFromPortal = player - (portal - player);
        Assert.True(FieldStreamingPolicy.ShouldPrefetch(player, towardPortal, portal, 100f),
            "movement toward Ricarten did not start prefetch");
        Assert.True(!FieldStreamingPolicy.ShouldPrefetch(player, awayFromPortal, portal, 300f),
            "movement away from Ricarten loaded it");
        Assert.True(walking > 1400f && sprinting > walking, "prefetch does not account for approach speed");
        Assert.True(FieldStreamingPolicy.RetentionDistance(300f) > sprinting, "streaming hysteresis is missing");
        Assert.True(FieldStreamingPolicy.PrefetchDistance(float.NaN) == 1400f, "invalid speed contaminated streaming radii");
        Assert.True(FieldStreamingPolicy.PrefetchDistance(float.MaxValue) == 4500f, "prefetch radius is unbounded");
        return Task.CompletedTask;
    }

    private static Task NpcApproachAsync()
    {
        var target = new Microsoft.Xna.Framework.Vector2(100, 0);
        var local = new Microsoft.Xna.Framework.Vector2(45, 0);
        var server = new Microsoft.Xna.Framework.Vector2(0, 0);
        var first = NpcApproach.Evaluate(local, server, target, false);
        Assert.True(first.StopLocally && first.SendStop && !first.OpenDialog, "near NPC did not stop prediction while authority caught up");

        var waiting = NpcApproach.Evaluate(local, server, target, true);
        Assert.True(waiting.StopLocally && !waiting.SendStop && !waiting.OpenDialog, "NPC wait resent movement every frame");

        var confirmed = NpcApproach.Evaluate(local, new Microsoft.Xna.Framework.Vector2(20, 0), target, true);
        Assert.True(confirmed.StopLocally && confirmed.OpenDialog, "authority-confirmed NPC range did not open interaction");

        var far = NpcApproach.Evaluate(Microsoft.Xna.Framework.Vector2.Zero, Microsoft.Xna.Framework.Vector2.Zero, target, false);
        Assert.True(far.BeginApproach && !far.StopLocally && far.Destination.X > 0 && far.Destination.X < target.X,
            "distant NPC did not produce a bounded approach destination");
        return Task.CompletedTask;
    }

    private static Task GateMovementAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "GateRunner");
        var player = new WorldPlayer(1, new Vector3(100, 0, 100), character, false, default);
        var destination = new Vector3(1000, 0, 100);
        player.SetTarget(destination, true);
        var previous = player.Position;
        for (var tick = 0; tick < 200; tick++)
        {
            if (tick == 20 || tick == 40)
            {
                player.CrossGate(tick == 20, new Vector3(5000, 0, 6000));
            }

            player.CommitStep(player.IntegrateStep(0.05f));
            Assert.True(player.Position.X >= previous.X, "crossing a gate reversed the run");
            previous = player.Position;
        }

        Assert.True(player.Position == destination, "gate cancelled the running destination");
        player.SetTarget(new Vector3(2000, 0, 100), true);
        var warp = new Vector3(9000, 0, 8000);
        player.EnterField(warp, true, warp);
        Assert.True(player.IntegrateStep(0.05f) == warp, "warp retained a destination from the old field");
        return Task.CompletedTask;
    }
}
