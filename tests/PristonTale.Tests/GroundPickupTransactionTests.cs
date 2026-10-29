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
using PristonTale.Contracts.World;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class GroundPickupTransactionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("pickup reserves through storage, rolls back failure and replays without duplication", DurableAsync);
        yield return ("a thrown item (the asked part of a stack) or gold leaves the character and lies on the ground where the player stands", ThrowAsync);
    }

    private static async Task ThrowAsync()
    {
        var potion = new ItemDefinition(101, "Mini Life Potion", ItemKind.Potion, MaxStack: 100) { CatalogKey = "PL101" };
        var sword = new ItemDefinition(102, "Short Sword", ItemKind.Sword, EquipmentSlot.Weapon) { CatalogKey = "WS101" };
        var quest = new ItemDefinition(103, "Quest Note", ItemKind.Quest) { CatalogKey = "QT101" };
        Assert.True(!ItemDropRules.CanDrop(ItemKind.Misc, "BI142") && !ItemDropRules.CanDrop(ItemKind.Amulet, "DA135")
            && !ItemDropRules.CanDrop(ItemKind.Ring, "OA231") && !ItemDropRules.CanDrop(ItemKind.Misc, "GF101") && ItemDropRules.CanDrop(ItemKind.Potion, "PL101")
            && ItemDropRules.CanDrop(ItemKind.Amulet, "DA130"), "the drop rules differ from the original's lists");
        var classes = new CharacterClassRegistry();
        var starter = CharacterRecordFactory.NewStarter(classes, 1, "Thrower", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var character = classes.CreatePlayer(PlayerClass.Fighter, starter.Name);
        character.RestoreProgress(0, 500);
        Assert.True(character.Inventory.TryAdd(new ItemInstance(potion, 5)) && character.Inventory.TryAdd(new ItemInstance(sword))
            && character.Inventory.TryAdd(new ItemInstance(quest)), "fixture items did not fit");
        var repository = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, 3, Vector3.Zero));
        var empty = WorldData.Empty;
        var holder = new WorldDataHolder
        {
            Current = new WorldData(new ItemCatalog([potion, sword, quest]), empty.Monsters, empty.Npcs, empty.FieldSpawns, empty.Motions)
        };
        var catalog = new WorldCatalog([new FieldDef("fixture", "Fixture", FieldState.Forest, new FieldBackdrop("", "", ""), "", []) { FieldCode = 3 }]);
        var world = new WorldServer(holder, catalog);
        var sessions = new SessionRegistry();
        var session = sessions.Create(1, "owner");
        var service = new SessionAppService(sessions, classes, repository, catalog, world, holder);
        Assert.True((await service.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "fixture entry failed");
        var outbox = new WorldOutbox();
        await WorldPump.RunAsync(world, world.JoinAsync(3, WorldPlayerFactory.Create(session), outbox.Writer, session));

        InventoryCommand Throw(ItemDefinition definition, int count)
        {
            var anchor = Enumerable.Range(0, session.Character!.Inventory.Capacity).First(index => session.Character.Inventory[index]?.Definition == definition);
            return new InventoryCommand(Guid.NewGuid(), session.Character.InventoryRevision, InventoryAction.Drop,
                session.Character.Inventory[anchor]!.InstanceId, 0, count, default);
        }

        Assert.True((await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, Throw(potion, 2)))).Ok, "two potions were not thrown");
        Assert.True((await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, Throw(sword, 1)))).Ok, "the sword was not thrown");
        var refused = await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, Throw(quest, 1)));
        Assert.True(!refused.Ok && refused.Message == ItemRuleMessages.CannotDrop, $"a quest item was thrown or refused as {refused.Message}");
        InventoryCommand Gold(int amount) =>
            new(Guid.NewGuid(), session.Character!.InventoryRevision, InventoryAction.DropGold, Guid.Empty, 0, amount, default);
        Assert.True(!(await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, Gold(501)))).Ok
            && (await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, Gold(120)))).Ok
            && session.Character!.Money == 380 && repository.Record.Money == 380, "gold beyond what is carried was thrown, or 120 gold was not taken and saved");
        var carried = session.Character!.Inventory;
        Assert.True(Enumerable.Range(0, carried.Capacity).Sum(index => carried[index]?.Definition == potion ? carried[index]!.Count : 0) == 3
            && Enumerable.Range(0, carried.Capacity).All(index => carried[index]?.Definition != sword)
            && Enumerable.Range(0, carried.Capacity).Any(index => carried[index]?.Definition == quest), "the bag kept the wrong items");

        GroundDropState[] ground = [];
        for (uint tick = 0; tick < 20 && ground.Length < 3; tick++)
        {
            world.Tick(0.05f, tick);
            while (outbox.Reader.TryRead(out var snapshot))
            {
                if (snapshot.GroundDrops.Length > 0)
                {
                    ground = snapshot.GroundDrops;
                }
            }
        }

        Assert.True(ground.Any(drop => drop.View.CatalogKey == "PL101" && drop.View.Amount == 2 && drop.Public)
            && ground.Any(drop => drop.View.CatalogKey == "WS101" && drop.View.Amount == 1)
            && ground.Any(drop => drop.View.Gold && drop.View.CatalogKey == ItemCodes.GoldKey && drop.View.Amount == 120 && drop.Public)
            && ground.All(drop => MathF.Max(MathF.Abs(drop.View.Position.X), MathF.Abs(drop.View.Position.Z)) == FieldGroundLoot.CellSpacing)
            && ground.Select(drop => drop.View.Position).Distinct().Count() == ground.Length,
            $"the ground holds {string.Join(", ", ground.Select(drop => $"{drop.View.CatalogKey} x{drop.View.Amount} at {drop.View.Position}"))}");
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
        await WorldPump.RunAsync(world, world.JoinAsync(3, WorldPlayerFactory.Create(session), outbox.Writer, session));
        WorldSnapshot snapshot = default;
        for (uint tick = 0; tick < 100; tick++)
        {
            world.Tick(0.05f, tick);
            if (outbox.Reader.TryRead(out snapshot) && snapshot.Entities.Any(entity => entity.Kind == EntityKind.Monster))
            {
                break;
            }
        }

        var target = snapshot.Entities.Single(entity => entity.Kind == EntityKind.Monster);
        await WorldPump.RunAsync(world, world.SetAttackTargetAsync(3, session.EntityId, target.EntityId, true));
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
        // A drop falls on a cell around the corpse; the client walks to it, once the killing swing ends, before picking it up.
        for (uint tick = 800; tick < 840; tick++)
        {
            world.Tick(0.05f, tick);
        }

        Assert.True(await WorldPump.RunAsync(world, world.ReportMovementAsync(3, session.EntityId, drop.Position, 0f, true, true, 0)) == MovementVerdict.Accepted,
            "the walk to the drop was refused");
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
        await WorldPump.RunAsync(world, writing.Task);
        Assert.True(session.Character.Money == money && !pending.IsCompleted, "pickup appeared before storage committed");
        storage.SetResult(false);
        Assert.True(!(await WorldPump.RunAsync(world, pending)).Ok, "storage rejection reported success");
        repository.BeforeUpdate = null;
        Assert.True((await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, command))).Ok, "failed storage left drop reserved");
        Assert.True(session.Character.Money == money + 8, "gold pickup changed the wrong amount");
        int writes = repository.UpdateCount;
        Assert.True((await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, command))).Ok && repository.UpdateCount == writes,
            "retry duplicated ground reward or storage");
        Assert.True(session.Character.Money == money + 8, "retry granted gold twice");
    }
}
