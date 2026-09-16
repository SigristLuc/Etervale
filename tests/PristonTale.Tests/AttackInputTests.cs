using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Gameplay.Combat;
using PristonTale.Simulation.Characters;

namespace PristonTale.Tests;

internal static class AttackInputTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("attack gestures distinguish tap, hold, Shift, release and stale acknowledgements", GesturesAsync);
        yield return ("a queued click survives the preceding swing and release does not queue an extra blow", QueuedAsync);
    }

    private static Task GesturesAsync()
    {
        var input = new AttackInputController();
        var press = input.Press(2, false, false);
        Assert.True(press.Sustained && input.Release(true, false) is null, "holding did not maintain attack");
        var release = input.Release(false, false)!.Value;
        Assert.True(!release.Sustained && release.Sequence == press.Sequence, "release created a new attack gesture");
        input.Reconcile(0, 0);
        Assert.True(input.TargetId == 2, "old idle snapshot cancelled a pending click");
        input.Reconcile(press.Sequence, 0);
        Assert.True(input.TargetId == 0, "completed tap kept inventing local attacks");
        var next = input.Press(2, true, true);
        Assert.True(next.Sequence > press.Sequence && input.Release(false, false) is null, "Shift did not latch across release");
        input.Reconcile(press.Sequence, 0);
        Assert.True(input.TargetId == 2, "old completion cancelled a fresh click on the same monster");
        input.Cancel();
        input.Reconcile(next.Sequence, 2);
        Assert.True(input.TargetId == 0, "late attack state undid movement cancellation");
        return Task.CompletedTask;
    }

    private static Task QueuedAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Queued");
        var player = new WorldPlayer(1, Vector3.Zero, character, false, default);
        player.AcceptAttackSequence(1);
        player.SetAttackTarget(2, true);
        player.BeginSwing(2, Vector3.One, 100, 50);
        player.SetAttackTarget(2, false); // Release of gesture 1.
        player.TickAction(100, out _);
        Assert.True(player.AttackTargetId == 0, "release queued an unwanted second attack");

        player.AcceptAttackSequence(2);
        player.SetAttackTarget(2, false);
        player.BeginSwing(2, Vector3.One, 100, 50);
        player.AcceptAttackSequence(3);
        player.SetAttackTarget(2, false); // A fresh press during the preceding swing.
        player.TickAction(100, out _);
        Assert.True(player.AttackTargetId == 2, "end of previous swing discarded a queued click");
        Assert.True(!player.AcceptAttackSequence(2), "stale command was accepted");
        player.BeginSwing(2, Vector3.One, 100, 50);
        player.TickAction(100, out _);
        Assert.True(player.AttackTargetId == 0, "queued tap did not end");

        var presence = new SubscriberPresence(1);
        presence.Diff([player.ToView()]);
        player.AcceptAttackSequence(4);
        var diff = presence.Diff([player.ToView()]);
        Assert.True(diff.States.Count == 1 && diff.States[0].AttackSequence == 4,
            "idle suppression dropped the acknowledgement of a rejected/finished command");
        return Task.CompletedTask;
    }
}
