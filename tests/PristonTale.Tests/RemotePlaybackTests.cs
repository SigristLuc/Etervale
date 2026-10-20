using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.World;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;

namespace PristonTale.Tests;

internal static class RemotePlaybackTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("remote snapshots interpolate position, server Y and speed on the server timeline", InterpolationAsync);
        yield return ("remote facing interpolates through the short arc across plus and minus pi", FacingWrapAsync);
        yield return ("remote samples snap when the authoritative error exceeds 256 units", SnapAsync);
        yield return ("remote extrapolation is capped at 250 ms and returns to the last sample", ExtrapolationAsync);
        yield return ("far-ring cadence raises the playback delay and the near ring restores it", CadenceAsync);
        yield return ("remote motion states become due when the render time reaches their sample", StateTimelineAsync);
        yield return ("remote clock follows early packets quickly, late packets slowly and resynchronises", ClockAsync);
        yield return ("remote Walk/Run without ground motion shows Stand and clip rate follows ground speed", LocomotionAsync);
        yield return ("remote player walk rate keeps the original frame step at nominal speed", PlayerRateAsync);
        yield return ("target frame follows the trace, keeps it after ground clicks and closes like the original", TargetSelectionAsync);
        yield return ("target HP bar is modern by default and classic only after a full-life attack", TargetHealthBarAsync);
        yield return ("presence clock extension round-trips and stamps decoded samples", PresenceClockAsync);
    }

    private static RemoteSample Sample(double time, float x, RemoteMoveState state = RemoteMoveState.Walking, float y = 0f, float facing = 0f) =>
        new(time, new Vector3(x, y, 0f), facing, state, 10);

    private static Task InterpolationAsync()
    {
        var buffer = new RemoteSnapshotBuffer(Sample(0, 0));
        Assert.True(!buffer.Add(Sample(200, 14, y: 10), Vector3.Zero), "a 14-unit step was treated as a snap");
        var pose = buffer.Sample(250);
        Assert.True(pose.Kind == RemotePoseKind.Interpolated, "render time between samples did not interpolate");
        Assert.Near(7f, pose.Position.X);
        Assert.Near(5f, pose.Position.Y);
        Assert.Near(70f, pose.PlanarSpeed, 0.01f);
        Assert.True(buffer.RenderTimeMs == 100, "playback delay is not the 150 ms base");
        return Task.CompletedTask;
    }

    private static Task FacingWrapAsync()
    {
        var buffer = new RemoteSnapshotBuffer(Sample(0, 0, facing: 3f));
        buffer.Add(Sample(200, 0, facing: -3f), Vector3.Zero);
        var facing = buffer.Sample(250).Facing;
        Assert.True(MathF.Abs(MathF.Abs(facing) - MathF.PI) < 1e-3f, $"facing crossed through zero instead of pi: {facing}");
        return Task.CompletedTask;
    }

    private static Task SnapAsync()
    {
        var buffer = new RemoteSnapshotBuffer(Sample(0, 0));
        Assert.True(buffer.Add(Sample(200, 300), Vector3.Zero), "a 300-unit jump was interpolated");
        Assert.True(buffer.Count == 1, "snap kept the previous history");
        Assert.Near(300f, buffer.Sample(250).Position.X);
        Assert.True(!buffer.Add(Sample(400, 310), new Vector3(300, 0, 0)), "a small step after a snap snapped again");
        Assert.True(buffer.Add(Sample(600, 320), new Vector3(0, 0, 0)), "a rendered error above 256 units did not snap");
        return Task.CompletedTask;
    }

    private static Task ExtrapolationAsync()
    {
        var buffer = new RemoteSnapshotBuffer(Sample(0, 0));
        buffer.Add(Sample(200, 14), Vector3.Zero);
        var lead = buffer.Sample(450);
        Assert.True(lead.Kind == RemotePoseKind.Extrapolated, "a walking entity without fresh samples froze");
        Assert.Near(21f, lead.Position.X, 0.001f);
        Assert.Near(70f, lead.PlanarSpeed, 0.01f);
        Assert.Near(31.5f, buffer.Sample(600).Position.X, 0.001f);
        var returning = buffer.Sample(650);
        Assert.Near(28f, returning.Position.X, 0.001f);
        Assert.True(returning.PlanarSpeed == 0f, "returning lead reported ground speed");
        var held = buffer.Sample(850);
        Assert.True(held.Kind == RemotePoseKind.Held && held.Position.X == 14f, "extrapolation stayed ahead of the last sample");

        var idle = new RemoteSnapshotBuffer(Sample(0, 0, RemoteMoveState.Idle));
        idle.Add(Sample(200, 14, RemoteMoveState.Idle), Vector3.Zero);
        Assert.True(idle.Sample(450).Position.X == 14f, "a standing sample was extrapolated");
        return Task.CompletedTask;
    }

    private static Task CadenceAsync()
    {
        var buffer = new RemoteSnapshotBuffer(Sample(0, 0));
        for (var index = 1; index <= 4; index++)
        {
            buffer.Add(Sample(index * 500, index * 35), Vector3.Zero);
        }

        Assert.True(buffer.TargetDelayMs == 450, $"2 Hz cadence chose delay {buffer.TargetDelayMs}");
        buffer.Advance(2000);
        Assert.True(buffer.DelayMs == 450, "delay did not grow to the far-ring cadence");
        var pose = buffer.Sample(2400);
        Assert.True(pose.Kind == RemotePoseKind.Interpolated, "far-ring entity stopped between packets");
        Assert.Near(136.5f, pose.Position.X, 0.01f);

        for (var index = 1; index <= 4; index++)
        {
            buffer.Add(Sample(2000 + index * 200, 140 + index * 14), Vector3.Zero);
        }

        buffer.Advance(10000);
        Assert.True(buffer.DelayMs == RemoteSnapshotBuffer.BaseDelayMs, "near-ring cadence did not restore the base delay");
        return Task.CompletedTask;
    }

    private static Task StateTimelineAsync()
    {
        var buffer = new RemoteSnapshotBuffer(Sample(0, 0, RemoteMoveState.Idle));
        buffer.Add(Sample(1000, 0, RemoteMoveState.Attack), Vector3.Zero);
        buffer.Sample(1100);
        Assert.True(!buffer.TryTakeDueState(out _), "attack state played before its sample time");
        buffer.Sample(1150);
        Assert.True(buffer.TryTakeDueState(out var due) && due.MoveState == RemoteMoveState.Attack, "attack state never became due");
        Assert.True(!buffer.TryTakeDueState(out _), "a state was delivered twice");
        return Task.CompletedTask;
    }

    private static Task ClockAsync()
    {
        var clock = new RemoteServerClock();
        Assert.True(clock.Stamp(null) == 0 && !clock.Synchronised, "unsynchronised clock did not use local time");
        clock.Observe(5000);
        Assert.True(clock.NowMs == 5000, "first server time did not synchronise");
        clock.Advance(100);
        Assert.True(clock.NowMs == 5100, "clock did not advance with local time");
        clock.Observe(5200);
        Assert.True(clock.NowMs == 5125, $"early packet moved the clock to {clock.NowMs}");
        clock.Observe(5000);
        Assert.True(Math.Abs(clock.NowMs - 5122.5) < 1e-9, $"late packet moved the clock to {clock.NowMs}");
        clock.Observe(9000);
        Assert.True(clock.NowMs == 9000, "a jump above one second did not resynchronise");
        Assert.True(clock.Stamp(42) == 42 && clock.Stamp(null) == clock.NowMs, "sample stamps ignored the server time");
        return Task.CompletedTask;
    }

    private static Task LocomotionAsync()
    {
        var scene = new RecordingScene();
        var monster = new RemoteModelEntity(scene, ActorHandle.None, 1f, 0, Vector3.Zero, 0f, RemoteMoveState.Idle, 10);
        var now = 1000d;
        void Run(double milliseconds)
        {
            for (var elapsed = 0d; elapsed < milliseconds; elapsed += 50)
            {
                now += 50;
                monster.Update(0.05f, now);
            }
        }

        monster.SetTarget(now, Vector3.Zero, 0f, RemoteMoveState.Walking, 10);
        Run(400);
        Assert.True(monster.Motion == PristonActorMotion.Walk && scene.LastMotion == PristonActorMotion.Stand,
            "a Walk state without ground motion was shown walking");

        var x = 0f;
        void Move(int samples, float step, RemoteMoveState state)
        {
            for (var index = 0; index < samples; index++)
            {
                x += step;
                monster.SetTarget(now, new Vector3(x, 0, 0), 0f, state, 10);
                Run(200);
            }
        }

        Move(6, 14, RemoteMoveState.Walking);
        Assert.True(scene.LastMotion == PristonActorMotion.Walk, "ground motion did not resume the walk clip");
        Assert.Near(1f, scene.LastRate, 0.02f);
        Move(6, 7, RemoteMoveState.Walking);
        Assert.Near(0.5f, scene.LastRate, 0.02f);
        Move(6, 28, RemoteMoveState.Running);
        Assert.True(scene.LastMotion == PristonActorMotion.Run, "run state was not shown");
        Assert.Near(1f, scene.LastRate, 0.02f);
        monster.SetTarget(now, new Vector3(x, 0, 0), 0f, RemoteMoveState.Attack, 10);
        Run(400);
        Assert.True(scene.LastMotion == PristonActorMotion.Attack, "server attack state lost to the locomotion safeguard");
        return Task.CompletedTask;
    }

    private static Task PlayerRateAsync()
    {
        var scene = new RecordingScene();
        var player = new RemoteEntity(scene, new PlayerActorHandles(ActorHandle.None, ActorHandle.None), 0, Vector3.Zero, 0f, RemoteMoveState.Walking, 10);
        player.SetRates(1, 1f);
        var speed = Locomotion.WalkUnitsPerSecond(1, inVillage: false);
        var now = 0d;
        for (var index = 1; index <= 8; index++)
        {
            player.SetTarget(index * 200, new Vector3(speed * index * 0.2f, 0, 0), 0f, RemoteMoveState.Walking, 10);
            for (var frame = 0; frame < 4; frame++)
            {
                now += 50;
                player.Update(0.05f, now);
            }
        }

        Assert.True(scene.LastMotion == PristonActorMotion.Walk, "walking player was not shown walking");
        Assert.Near(AnimationRate.MoveRate(Locomotion.BaseTranslation(1)), scene.LastRate, 0.01f);
        return Task.CompletedTask;
    }

    private static Task TargetSelectionAsync()
    {
        var live = new Dictionary<uint, TargetLiveness> { [2] = TargetLiveness.Alive, [3] = TargetLiveness.Alive, [4] = TargetLiveness.Alive };
        TargetLiveness Query(uint id) => live.TryGetValue(id, out var state) ? state : TargetLiveness.Missing;
        var selection = new TargetSelection();
        selection.Trace(2);
        selection.Update(0, Query);
        Assert.True(selection.FrameId == 2, "clicked monster did not open the frame");
        selection.Hover(3);
        selection.Update(10, Query);
        Assert.True(selection.FrameId == 2, "hover replaced a traced target");
        selection.Hover(0);
        selection.ClearTrace();
        selection.Update(20, Query);
        Assert.True(selection.FrameId == 2, "a ground click closed the frame");
        selection.Hover(3);
        selection.Update(30, Query);
        Assert.True(selection.FrameId == 3, "hover did not replace the frame without a trace");
        selection.Hover(0);
        selection.Update(40, Query);
        Assert.True(selection.FrameId == 3, "leaving the hovered entity closed the frame");
        selection.Trace(4);
        selection.Update(50, Query);
        Assert.True(selection.FrameId == 4, "clicked NPC did not open the frame");

        live[4] = TargetLiveness.Dead;
        selection.Update(100, Query);
        Assert.True(selection.FrameId == 4 && selection.TracedId == 0, "death closed the frame at once or kept the trace");
        selection.Update(100 + 1142, Query);
        Assert.True(selection.FrameId == 4, "frame closed before the 0x50-frame death delay");
        selection.Update(100 + 1143, Query);
        Assert.True(selection.FrameId == 0, "dead target kept the frame after its death delay");

        selection.Trace(2);
        selection.Update(1300, Query);
        live.Remove(2);
        selection.Update(1310, Query);
        Assert.True(selection.FrameId == 0 && selection.TracedId == 0, "removed target kept the frame");
        live[3] = TargetLiveness.Dead;
        selection.Hover(3);
        selection.Update(1320, Query);
        Assert.True(selection.FrameId == 0, "hovering a corpse opened the frame");
        selection.Trace(3);
        selection.Clear();
        Assert.True(selection.FrameId == 0 && selection.TracedId == 0 && selection.HoveredId == 0, "clear kept selection state");
        return Task.CompletedTask;
    }

    private static Task TargetHealthBarAsync()
    {
        var combat = new HudCombat();
        var appearance = new PlayerAppearanceInfo("Self", 1, 0, 0, 0, "", "", 1);
        combat.Observe(new PresenceUpdate(1,
        [
            new(1, RemoteEntityKind.Player, true, default, 0, RemoteMoveState.Idle, 64, 64, appearance, 0),
            new(2, RemoteEntityKind.Monster, false, default, 0, RemoteMoveState.Idle, 50, 50, null, 1, Name: "Hopy", Level: 3),
            new(3, RemoteEntityKind.Monster, false, default, 0, RemoteMoveState.Idle, 20, 50, null, 1),
            new(4, RemoteEntityKind.Npc, false, default, 0, RemoteMoveState.Idle, 0, 0, null, 7)
        ], [], [],
        [
            new(1, 2, RemoteCombatEventKind.SwingBegin, 800, 400, 0, false, false),
            new(1, 3, RemoteCombatEventKind.SwingBegin, 800, 400, 0, false, false)
        ]));
        Assert.True(combat.ShowsHealthBar(2, classic: false) && combat.ShowsHealthBar(3, classic: false), "modern mode hid a monster bar");
        Assert.True(combat.ShowsHealthBar(2, classic: true), "classic mode hid the bar after a full-life attack");
        Assert.True(!combat.ShowsHealthBar(3, classic: true), "classic mode showed a bar for a monster someone else engaged");
        Assert.True(!combat.ShowsHealthBar(4, classic: false), "an NPC without life showed a bar");
        Assert.True(HudCombat.HealthPercent(1, 1000) == 1 && HudCombat.HealthPercent(0, 10) == 0, "percentage hid a living target or showed a dead one");
        Assert.True(HudCombat.HealthPercent(500, 1000) == 50 && HudCombat.HealthPercent(1000, 1000) == 100, "percentage formula drifted");
        foreach (var size in new[] { new Point(800, 600), new Point(1920, 1080), new Point(3840, 2160) })
        {
            var layout = HudLayout.Create(size.X, size.Y);
            var frame = HudCombat.TargetFrame(layout);
            var viewport = new Rectangle(0, 0, layout.Width, layout.Height);
            Assert.True(frame.Box.Contains(frame.Portrait) && viewport.Contains(frame.HealthBar), "target frame regions escaped their bounds");
            Assert.True(frame.LevelY >= frame.HealthBar.Bottom && frame.NameY < frame.Box.Bottom, "target frame lines overlap the bar");
        }

        return Task.CompletedTask;
    }

    private static Task PresenceClockAsync()
    {
        var buffer = new byte[PresenceClockPacket.Size];
        Assert.True(new PresenceClockPacket(1234, 50).Write(buffer) == PresenceClockPacket.Size, "presence clock size drifted");
        var read = PresenceClockPacket.Read(buffer);
        Assert.True(read.ServerTick == 1234 && read.TickIntervalMs == 50 && read.ServerTimeMs == 61700, "presence clock fields did not round-trip");
        var rejected = (byte[])buffer.Clone();
        rejected[8] = 0;
        rejected[9] = 0;
        var threw = false;
        try
        {
            PresenceClockPacket.Read(rejected);
        }
        catch (InvalidDataException)
        {
            threw = true;
        }

        Assert.True(threw, "presence clock version 0 was accepted");

        var decoder = new PresenceDecoder(new WorldRenderCatalog(), static _ => { });
        Assert.True(decoder.TryHandle(buffer, out var none) && none is null, "presence clock was not consumed by the decoder");
        Assert.True(decoder.ServerTimeMs == 61700, "decoder ignored the presence clock");
        return Task.CompletedTask;
    }

    private sealed class RecordingScene : IActorScene
    {
        public PristonActorMotion LastMotion { get; private set; } = PristonActorMotion.Stand;
        public float LastRate { get; private set; } = 1f;
        public ActorHandle AddActor(PristonActorInstance instance) => ActorHandle.None;
        public void SetActorWorld(ActorHandle handle, Matrix world)
        {
        }

        public void SetActorMotion(ActorHandle handle, PristonActorMotion motion) => LastMotion = motion;
        public void SetActorMotionIndex(ActorHandle handle, int motionIndex)
        {
        }

        public void RestartActorMotion(ActorHandle handle, PristonActorMotion motion) => LastMotion = motion;
        public void SetActorPlaybackRate(ActorHandle handle, float rateFactor) => LastRate = rateFactor;
        public void RemoveActor(ActorHandle handle)
        {
        }
    }
}
