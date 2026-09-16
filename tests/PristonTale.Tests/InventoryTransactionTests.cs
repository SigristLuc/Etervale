using System.Numerics;
using System.Threading.Channels;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

internal static class InventoryTransactionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("inventory commits only after storage and retries survive reconnect", DurabilityAsync);
    }

    private static async Task DurabilityAsync()
    {
        var classes = new CharacterClassRegistry();
        var starter = CharacterRecordFactory.NewStarter(classes, 1, "Durable", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var character = classes.CreatePlayer(PlayerClass.Fighter, starter.Name);
        var item = new ItemInstance(new ItemDefinition(1, "Stone", ItemKind.Material));
        character.Inventory.TryPlace(item, 0);
        var repository = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, 3, Vector3.Zero));
        var data = WorldData.Empty;
        var holder = new WorldDataHolder
        {
            Current = new WorldData(new ItemCatalog([item.Definition]), data.Monsters, data.Npcs, data.FieldSpawns, data.Motions)
        };
        var catalog = new WorldCatalog();
        var world = new WorldServer(holder, catalog);
        var sessions = new SessionRegistry();
        var session = sessions.Create(1, "owner");
        var service = new SessionAppService(sessions, classes, repository, catalog, world, holder);
        Assert.True((await service.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "setup entry failed");
        var outbound = Channel.CreateUnbounded<WorldSnapshot>();
        await AdvanceAsync(world, world.JoinAsync(3, WorldPlayerFactory.Create(session), outbound.Writer, session));
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.BeforeUpdate = _ =>
        {
            writing.TrySetResult();
            return storage.Task;
        };
        var command = new InventoryCommand(Guid.NewGuid(), session.Character!.InventoryRevision, InventoryAction.Move,
            item.InstanceId, 2, 0, default);
        var pending = service.ApplyInventoryAsync(session.SessionToken, command);
        await AdvanceAsync(world, writing.Task);
        Assert.True(!pending.IsCompleted && session.Character.Inventory[0] is not null && session.Character.Inventory[2] is null,
            "pending storage exposed uncommitted inventory");
        var competing = await service.ApplyInventoryAsync(session.SessionToken, command);
        Assert.True(!competing.Ok, "concurrent operation bypassed save admission");
        storage.SetResult(false);
        Assert.True(!(await pending).Ok && session.Character.Inventory[0] is not null, "storage rejection changed live inventory");
        repository.BeforeUpdate = null;
        Assert.True((await AdvanceAsync(world, service.ApplyInventoryAsync(session.SessionToken, command))).Ok, "valid retry failed");
        Assert.True(session.Character.Inventory[0] is null && session.Character.Inventory[2].InstanceId == item.InstanceId, "durable move not committed");
        var writes = repository.UpdateCount;
        Assert.True((await AdvanceAsync(world, service.ApplyInventoryAsync(session.SessionToken, command))).Ok, "idempotent retry failed");
        Assert.True(repository.UpdateCount == writes, "idempotent retry wrote or mutated a second time");
        await AdvanceAsync(world, world.LeaveAsync(3, session.EntityId, outbound.Writer));
        sessions.Remove(session.SessionToken);
        var reconnected = sessions.Create(1, "reconnected");
        Assert.True((await service.EnterWorldAsync(reconnected.SessionToken, starter.Name)).Ok && reconnected.TryBeginPlay(), "reconnect failed");
        var nextOutbound = Channel.CreateUnbounded<WorldSnapshot>();
        await AdvanceAsync(world, world.JoinAsync(3, WorldPlayerFactory.Create(reconnected), nextOutbound.Writer, reconnected));
        var replay = await AdvanceAsync(world, service.ApplyInventoryAsync(reconnected.SessionToken, command));
        Assert.True(replay.Ok && repository.UpdateCount == writes && reconnected.Character!.Inventory[2].InstanceId == item.InstanceId,
            "reconnect lost idempotency history or item identity");
    }

    private static async Task AdvanceAsync(WorldServer world, Task task)
    {
        for (uint tick = 0; !task.IsCompleted && tick < 1000; tick++)
        {
            world.Tick(0.05f, tick);
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
