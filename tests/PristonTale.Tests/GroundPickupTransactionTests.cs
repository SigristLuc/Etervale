using System.Numerics;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Tests;

internal static class GroundPickupTransactionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("pickup reserves through storage, rolls back failure and replays without duplication", DurableAsync);
    }

    private static async Task DurableAsync()
    {
        var classes = new CharacterClassRegistry();
        var starter = CharacterRecordFactory.NewStarter(classes, 1, "Loot", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var character = classes.CreatePlayer(PlayerClass.Fighter, starter.Name);
        var repository = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, 3, Vector3.Zero));
        var monster = new MonsterDefinition(1, "Loot fixture", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(4, 1, new MonsterRange(1, 1), 6, 30, 0, 0, 0, 1, ResistanceBlock.Zero), "")
        {
            ExpReward = 105,
            Drops = new DropTable([DropEntry.GoldSlot(100, 8, 8)], 1, false),
            // Monsters walk through players as in the original, so the fixture chases the player to stay in reach.
            Ai = new MonsterAi(5, MonsterNature.Evil, 200, 1, 0, MonsterRange.Zero, MonsterActiveHour.Any)
        };
        var spawn = new FieldSpawnData([new SpawnPoint(24, 0, true)], [new SpawnTableEntry(1, 100)], [], [])
        {
            MaxConcurrentMonsters = 1,
            MaxPerSpawnPoint = 1
        };
        var empty = WorldData.Empty;
        var holder = new WorldDataHolder
        {
            Current = new WorldData(empty.Items, new MonsterCatalog([monster]), empty.Npcs,
                new Dictionary<int, FieldSpawnData> { [3] = spawn }, empty.Motions)
        };
        var catalog = new WorldCatalog([new FieldDef("fixture", "Fixture", FieldState.Forest, new FieldBackdrop("", "", ""), "", []) { FieldCode = 3 }]);
        var world = new WorldServer(holder, catalog);
        var sessions = new SessionRegistry();
        var session = sessions.Create(1, "owner");
        var service = new SessionAppService(sessions, classes, repository, catalog, world, holder);
        Assert.True((await service.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "fixture entry failed");
        var outbox = new WorldOutbox();
        await AdvanceAsync(world, world.JoinAsync(3, WorldPlayerFactory.Create(session), outbox.Writer, session));
        WorldSnapshot snapshot = default;
        for (uint tick = 0; tick < 100; tick++)
        {
            world.Tick(0.05f, tick);
            if (outbox.Reader.TryRead(out snapshot) && snapshot.Entities.Any(entity => entity.Kind == EntityKindView.Monster))
            {
                break;
            }
        }

        var target = snapshot.Entities.Single(entity => entity.Kind == EntityKindView.Monster);
        await AdvanceAsync(world, world.SetAttackTargetAsync(3, session.EntityId, target.EntityId, true));
        for (uint tick = 0; tick < 800; tick++)
        {
            world.Tick(0.05f, tick);
            if (outbox.Reader.TryRead(out snapshot) && snapshot.GroundDrops.Length > 0)
            {
                break;
            }
        }

        var drop = snapshot.GroundDrops.Single().View;
        Assert.True(session.Character!.Experience == 525, "production host omitted configured XP bonus");
        long money = session.Character.Money;
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.BeforeUpdate = _ =>
        {
            writing.TrySetResult();
            return storage.Task;
        };
        var command = new InventoryCommand(Guid.NewGuid(), session.Character.InventoryRevision, InventoryAction.Pickup, drop.Id, 0, 1, default);
        var pending = service.ApplyInventoryAsync(session.SessionToken, command);
        await AdvanceAsync(world, writing.Task);
        Assert.True(session.Character.Money == money && !pending.IsCompleted, "pickup appeared before storage committed");
        storage.SetResult(false);
        Assert.True(!(await AdvanceAsync(world, pending)).Ok, "storage rejection reported success");
        repository.BeforeUpdate = null;
        Assert.True((await AdvanceAsync(world, service.ApplyInventoryAsync(session.SessionToken, command))).Ok, "failed storage left drop reserved");
        Assert.True(session.Character.Money == money + 8, "gold pickup changed the wrong amount");
        int writes = repository.UpdateCount;
        Assert.True((await AdvanceAsync(world, service.ApplyInventoryAsync(session.SessionToken, command))).Ok && repository.UpdateCount == writes,
            "retry duplicated ground reward or storage");
        Assert.True(session.Character.Money == money + 8, "retry granted gold twice");
    }

    private static async Task AdvanceAsync(WorldServer world, Task task)
    {
        for (uint tick = 0; !task.IsCompleted && tick < 1000; tick++)
        {
            world.Tick(.05f, tick);
            await Task.Delay(1);
        }

        await task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    private static async Task<T> AdvanceAsync<T>(WorldServer world, Task<T> task)
    {
        await AdvanceAsync(world, (Task)task);
        return await task;
    }
}
