using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Application.World;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Protocol.Binary;
using PristonTale.Simulation.Characters;

namespace PristonTale.Tests;

internal static class ClientTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("original presence packets retain combat events, skill motion and field identity", PresenceAsync);
        yield return ("crowded presence keeps position updates inside the per-receiver budget without starving walkers", CrowdBudgetAsync);
        yield return ("remote rates and explicit swing events replace per-frame restarts", RemoteMotionAsync);
        yield return ("NPC duplicate Stand clips preserve exact INX indices and weighted selection", NpcStandMotionsAsync);
        yield return ("pending actor preparation does not block presence or resurrect removals", PendingActorAsync);
        yield return ("stationary NPC floor queries invalidate on movement and geometry residency", NpcFloorAsync);
        yield return ("NPC, monster and player collision survives pending assets and ignores visual floor height", CollisionRosterAsync);
        yield return ("cursor rays originate at the camera and reject an enclosing NPC pick volume", CursorPickingAsync);
    }

    private static Task PresenceAsync()
    {
        var origin = new System.Numerics.Vector3(120, 35, -400);
        var impact = new CombatEventView(9, 1, CombatEventKindView.MonsterSkillImpact,
            sourceSoundCode: 0x18E0, position: origin, facing: 1.2f);
        var entity = new EntityView(2, EntityKindView.Monster, false, origin, 1.2f, MoveStateView.Skill,
            10, 45, 60, default, templateId: 1);
        var self = new EntityView(1, EntityKindView.Player, false, origin, 0f, MoveStateView.Idle, 1, 100, 100,
            new PlayerAppearanceView("Self", 2, 0, 0, 0, "", ""));
        var snapshot = new WorldSnapshot(8, [self, entity], [impact], 3);
        var update = new WireRoundTrip(1).PushMerged(snapshot);
        Assert.True(update.FieldId == 3 && update.Spawns.Single(spawn => spawn.EntityId == 2).MoveState == RemoteMoveState.Skill,
            "TCP presence dropped field or skill motion");
        Assert.True(update.Spawns.Single(spawn => spawn.EntityId == 1).IsSelf, "own avatar was not marked as self");
        Assert.True(update.Events.Single().Position == origin && update.Events.Single().Kind == RemoteCombatEventKind.MonsterSkillImpact,
            "TCP presence dropped skill impact data");
        return Task.CompletedTask;
    }

    private static Task CrowdBudgetAsync()
    {
        EntityView Walker(int id, float offset) => new((uint)id, EntityKindView.Player, false, new System.Numerics.Vector3(id + offset, 0, id % 20 * 10f), 0f,
            MoveStateView.Walking, 1, 100, 100, new PlayerAppearanceView("W" + id, 2, 0, 0, 0, "", ""));
        var wire = new WireRoundTrip(1);
        var spawned = wire.PushMerged(new WorldSnapshot(1, Enumerable.Range(1, 300).Select(id => Walker(id, 0)).ToArray(), fieldId: 3));
        Assert.True(spawned.Spawns.Count == 300, "spawns were held back by the movement budget");
        var updated = new HashSet<uint>();
        for (uint tick = 2; tick < 42; tick++)
        {
            var before = wire.BytesSent;
            var update = wire.PushMerged(new WorldSnapshot(tick, Enumerable.Range(1, 300).Select(id => Walker(id, tick)).ToArray(), fieldId: 3));
            // Budget for others, plus the receiver's own entry and at most two PLAYDATAGROUP headers.
            Assert.True(wire.BytesSent - before <= 2048 + 84 + 2 * 16, "a crowd exceeded the per-receiver movement budget");
            updated.UnionWith(update.States.Select(state => state.EntityId));
        }

        Assert.True(updated.Count == 300, "age weighting starved part of the crowd");
        return Task.CompletedTask;
    }

    private static Task RemoteMotionAsync()
    {
        var scene = new FakeScene();
        var remote = new RemoteModelEntity(
            scene,
            ActorHandle.None,
            1,
            Vector3.Zero,
            0,
            RemoteMoveState.Attack,
            100);
        remote.SetRates(5, 1.75f);
        remote.ApplyCombatEvent(new CombatEventInfo(
            2,
            1,
            RemoteCombatEventKind.SwingBegin,
            800,
            400,
            0,
            false,
            false));
        remote.Update(0.05f);
        Assert.True(scene.Restarts == 1, "swing did not restart exactly once");
        Assert.Near(1.75f, scene.LastRate);
        var commands = scene.Commands;
        for (var index = 0; index < 10; index++)
        {
            remote.Update(0.016f);
        }

        Assert.True(scene.Commands == commands, "held Attack emitted per-frame commands");
        remote.ApplyCombatEvent(new CombatEventInfo(
            2,
            1,
            RemoteCombatEventKind.SwingBegin,
            800,
            400,
            0,
            false,
            false));
        remote.Update(0.05f);
        Assert.True(scene.Restarts == 2, "second swing event was lost");
        remote.SetTarget(Vector3.Zero, 0, RemoteMoveState.Skill, 100);
        var cast = new CombatEventInfo(2, 1, RemoteCombatEventKind.SwingBegin, 1980, 957, 0, false, false);
        remote.ApplyCombatEvent(cast);
        remote.Update(.05f);
        Assert.True(scene.Restarts == 3 && remote.Motion == PristonActorMotion.Skill, "skill entry restarted basic attack");
        remote.ApplyCombatEvent(cast);
        remote.Update(.05f);
        Assert.True(scene.Restarts == 4 && remote.Motion == PristonActorMotion.Skill, "repeated skill did not restart its own motion");
        remote.SetTarget(Vector3.Zero, 0, RemoteMoveState.Walking, 100);
        remote.Update(.05f);
        Assert.Near(1f, scene.LastRate);
        remote.SetTarget(Vector3.Zero, 0, RemoteMoveState.Running, 100);
        remote.Update(.05f);
        Assert.Near(1f, scene.LastRate);
        return Task.CompletedTask;
    }

    private static Task NpcStandMotionsAsync()
    {
        var motions = Enumerable.Range(0, 13)
            .Select(static _ => Motion(0, 0, 0))
            .ToArray();
        motions[10] = Motion((uint)PristonActorMotion.Walk, 11, 41);
        motions[11] = Motion((uint)PristonActorMotion.Stand, 50, 90);
        motions[12] = Motion((uint)PristonActorMotion.Stand, 100, 160);
        var warehouseInfo = ModelInfo("TN-010.inx", motions);
        var cycle = new NpcStandMotionCycle(warehouseInfo, 91);
        var selected = new HashSet<int>();
        for (var index = 0; index < 32; index++)
        {
            var update = cycle.Update(2f, PristonActorMotion.Stand);
            Assert.True(update.MotionChanged, "completed NPC Stand cycle did not select another concrete clip");
            Assert.True(update.MotionIndex is 11 or 12, "NPC idle selected Walk or an empty reserved INX slot");
            selected.Add(update.MotionIndex);
        }

        Assert.True(selected.SetEquals([11, 12]), "warehouse keeper never reached every authored Stand gesture");

        var weightedRates = new int[30];
        weightedRates[PristonFormatConstants.CharacterMotionBase] = 100;
        var weightedInfo = warehouseInfo with
        {
            NpcMotionRates = weightedRates,
            NpcMotionSelection = Enumerable.Repeat(12, PristonFormatConstants.MotionRateSelectionCount).ToArray()
        };
        var weighted = new NpcStandMotionCycle(weightedInfo, 17).Update(0f, PristonActorMotion.Stand);
        Assert.True(weighted.MotionIndex == 12, "expanded Win32 NPC motion-rate table was ignored");

        var scene = new FakeScene();
        var npc = new RemoteModelEntity(
            scene,
            ActorHandle.None,
            1f,
            Vector3.Zero,
            0f,
            RemoteMoveState.Idle,
            1,
            RemoteEntityKind.Npc,
            modelInfo: warehouseInfo,
            entityId: 91);
        npc.Update(0f);
        Assert.True(scene.ExactMotionIndices.Count == 1 && scene.ExactMotionIndices[0] is 11 or 12, "remote NPC collapsed its selected Stand entry back to the enum");
        return Task.CompletedTask;
    }

    private static PristonMotionInfo Motion(uint state, uint startFrame, uint endFrame)
    {
        return new PristonMotionInfo(state, startFrame, endFrame, [], 0, 0, 0, 1, 0, 0);
    }

    private static PristonModelInfo ModelInfo(string path, IReadOnlyList<PristonMotionInfo> motions)
    {
        var emptyGroup = new PristonModelGroup(0, []);
        return new PristonModelInfo(path, "fixture.smd", "fixture.smd", "", emptyGroup, emptyGroup, emptyGroup, motions, (uint)motions.Count, "", "", "", [], 0);
    }

    private static Task PendingActorAsync()
    {
        var scene = new FakeScene();
        var composer = new PendingComposer();
        var manager = new RemoteEntityManager(scene, composer, composer, new CharacterClassRegistry(), ".");
        var appearance = new PlayerAppearanceInfo(
            "remote",
            1,
            0,
            0,
            0,
            "",
            "",
            1);
        var spawn = new EntitySpawnInfo(
            2,
            RemoteEntityKind.Player,
            false,
            System.Numerics.Vector3.Zero,
            0,
            RemoteMoveState.Idle,
            100,
            100,
            appearance,
            0);
        manager.Apply(new PresenceUpdate(1, [spawn], [], [], []));
        Assert.True(manager.Count == 0 && composer.Composed == 0, "unprepared actor was composed");
        manager.Apply(new PresenceUpdate(2, [], [], [2], []));
        composer.Ready.TrySetResult();
        manager.Update(0.05f);
        Assert.True(manager.Count == 0 && composer.Composed == 0, "removed pending actor reappeared");
        return Task.CompletedTask;
    }

    private static Task NpcFloorAsync()
    {
        var remote = new RemoteModelEntity(new FakeScene(), ActorHandle.None, 1, Vector3.Zero, 0,
            RemoteMoveState.Idle, 1, RemoteEntityKind.Npc);
        var calls = 0;
        var height = 7f;
        var found = true;
        bool Floor(float x, float z, out float y)
        {
            calls++;
            y = height;
            return found;
        }

        PristonTale.Gameplay.World.Remote.FloorHeightQuery query = Floor;
        for (var index = 0; index < 120; index++)
        {
            remote.Update(1f / 120, query, 0);
        }

        Assert.True(calls == 1 && remote.DrawnPosition.Y == 7, "stationary NPC repeated the geometry query");
        height = 11;
        remote.Update(1f / 120, query, 1);
        Assert.True(calls == 2 && remote.DrawnPosition.Y == 11, "loaded geometry did not invalidate the cached height");
        remote.SetTarget(new Vector3(0.5f, 0, 0), 0, RemoteMoveState.Idle, 1);
        remote.Update(1f / 120, query, 1);
        Assert.True(calls == 3 && remote.DrawnPosition.X == 0.5f, "movement reused another position's height");
        found = false;
        remote.Update(1f / 120, query, 2);
        remote.Update(1f / 120, query, 2);
        Assert.True(calls == 4 && remote.DrawnPosition.Y == 0, "unloaded terrain retained stale height or repeated a cached miss");
        found = true;
        remote.Update(1f / 120, query, 3);
        Assert.True(calls == 5 && remote.DrawnPosition.Y == 11, "a cached miss survived geometry publication");
        return Task.CompletedTask;
    }

    private static Task CollisionRosterAsync()
    {
        var composer = new PendingComposer();
        var manager = new RemoteEntityManager(new FakeScene(), composer, composer, new CharacterClassRegistry(), ".");
        var appearance = new PlayerAppearanceInfo("Remote", 1, 0, 0, 0, "", "", 1);
        var origin = new System.Numerics.Vector3(0, 7, 0);
        manager.Apply(new PresenceUpdate(1,
        [
            new(1, RemoteEntityKind.Player, true, origin, 0, RemoteMoveState.Idle, 100, 100, appearance, 0),
            new(2, RemoteEntityKind.Monster, false, origin + new System.Numerics.Vector3(40, 0, 0), 0, RemoteMoveState.Idle, 10, 10, null, 1),
            new(3, RemoteEntityKind.Npc, false, origin + new System.Numerics.Vector3(0, 0, 40), 0, RemoteMoveState.Idle, 1, 1, null, 2),
            new(4, RemoteEntityKind.Player, false, origin + new System.Numerics.Vector3(-25, 0, 0), 0, RemoteMoveState.Idle, 100, 100, appearance, 0)
        ], [], [], []));
        var neighbours = new List<PristonTale.Simulation.Physics.EntityNeighbour>();
        manager.CollectNeighbours(1, neighbours);
        Assert.True(manager.Count == 0 && neighbours.Count == 3, "asset preparation disabled authoritative blockers");
        foreach (var displacement in new[] { new System.Numerics.Vector3(30, 0, 0), new(0, 0, 30), new(-15, 0, 0) })
        {
            var accepted = PristonTale.Simulation.Physics.EntityCollision.Resolve(origin, origin + displacement, 16, neighbours);
            Assert.True(accepted == origin, "client predicted movement through a live entity");
        }

        manager.SetFloorQuery(static (float x, float z, out float y) =>
        {
            y = 1000;
            return true;
        });
        composer.Ready.TrySetResult();
        manager.Update(0.1f);
        manager.CollectNeighbours(1, neighbours);
        Assert.True(neighbours.All(neighbour => neighbour.Position.Y == 7), "visual floor height leaked into authority collision space");
        manager.Apply(new PresenceUpdate(2, [], [], [2, 3], []));
        manager.CollectNeighbours(1, neighbours);
        Assert.True(neighbours.Count == 1, "removed pending entities left phantom blockers");
        manager.Clear();
        manager.CollectNeighbours(1, neighbours);
        Assert.True(neighbours.Count == 0, "field teardown retained collision state");
        return Task.CompletedTask;
    }

    private static Task CursorPickingAsync()
    {
        var viewport = new Viewport(0, 0, 800, 600);
        var camera = new Vector3(0, 0, 10);
        var view = Matrix.CreateLookAt(camera, Vector3.Zero, Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, viewport.AspectRatio, 1f, 100f);
        Assert.True(
            CursorPicking.TryCreateRay(viewport, projection, view, camera, new Point(400, 300), out var centerRay),
            "center cursor did not produce a ray");
        Assert.True(centerRay.Position == camera, "cursor ray started on the near plane instead of the camera");
        Assert.True(CursorPicking.IntersectSphere(centerRay, Vector3.Zero, 1f) is not null, "center cursor missed the visible entity");

        Assert.True(
            CursorPicking.TryCreateRay(viewport, projection, view, camera, new Point(20, 20), out var cornerRay),
            "corner cursor did not produce a ray");
        Assert.True(CursorPicking.IntersectSphere(cornerRay, Vector3.Zero, 1f) is null, "off-entity cursor selected the entity");

        var enclosingRay = new Ray(Vector3.Zero, Vector3.Forward);
        Assert.True(CursorPicking.IntersectSphere(enclosingRay, Vector3.Zero, 40f) is null, "camera inside an NPC volume made every cursor direction a hit");
        return Task.CompletedTask;
    }

    private sealed class PendingComposer : IPlayerActorComposer, IModelActorComposer
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Composed { get; private set; }
        public Task PrepareAsync(string root, CharacterVisualSet visuals, int face) => Ready.Task;
        public Task PrepareAsync(string root, string model) => Ready.Task;
        public PlayerActorHandles Compose(string root, CharacterVisualSet visuals, int face, Matrix world, PristonActorMotion motion)
        {
            Composed++;
            return new PlayerActorHandles(ActorHandle.None, ActorHandle.None);
        }

        public ComposedModelActor Compose(string root, string model, Matrix world, PristonActorMotion motion)
        {
            Composed++;
            return new ComposedModelActor(ActorHandle.None, null);
        }
    }

    private sealed class FakeScene : IActorScene
    {
        public int Restarts { get; private set; }
        public int Commands { get; private set; }
        public float LastRate { get; private set; }
        public List<int> ExactMotionIndices { get; } = [];
        public ActorHandle AddActor(PristonActorInstance instance) => ActorHandle.None;
        public void SetActorWorld(ActorHandle handle, Matrix world)
        {
        }
        public void SetActorMotion(ActorHandle handle, PristonActorMotion motion) => Commands++;
        public void SetActorMotionIndex(ActorHandle handle, int motionIndex)
        {
            ExactMotionIndices.Add(motionIndex);
            Commands++;
        }
        public void RestartActorMotion(ActorHandle handle, PristonActorMotion motion)
        {
            Restarts++;
            Commands++;
        }

        public void SetActorPlaybackRate(ActorHandle handle, float rateFactor) => LastRate = rateFactor;
        public void RemoveActor(ActorHandle handle)
        {
        }
    }
}
