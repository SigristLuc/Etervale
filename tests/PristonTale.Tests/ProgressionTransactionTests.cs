using System.Numerics;
using System.Threading.Channels;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

internal static class ProgressionTransactionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("all 40 first-tier skills enforce ten ranks, predecessor and original gold costs", LearningAsync);
        yield return ("NPC learning is durable, rejects remote service access and replays after reconnect", DurabilityAsync);
    }

    private static Task LearningAsync()
    {
        var registry = new CharacterClassRegistry();
        foreach (var playerClass in Enum.GetValues<PlayerClass>())
        {
            var skills = FirstTierSkills.For(playerClass);
            var player = registry.CreatePlayer(playerClass, "Learning", 40);
            player.RestoreProgress(ExperienceTable.ForLevel(40), 1000000, skillPoints: 40);
            Assert.True(!SkillLearning.TryQuote(player, skills[1].Code, out _), "predecessor requirement bypassed");
            var spent = 0;
            for (var rank = 0; rank < 10; rank++)
            {
                for (var index = 0; index < 4; index++)
                {
                    var expectedPrice = (index + 1) * 500 + rank * (500 + index * 100);
                    Assert.True(SkillLearning.TryQuote(player, skills[index].Code, out var price) && price == expectedPrice, "original learning price differs");
                    var command = new ProgressionCommand(Guid.NewGuid(), player.ProgressionRevision, ProgressionAction.LearnSkill,
                        SkillCode: skills[index].Code, NpcEntityId: 1);
                    Assert.True(player.ApplyProgression(command) && player.Skills.RankOf(skills[index].Code) == rank + 1, "rank did not advance once");
                    Assert.True(!player.ApplyProgression(command), "stale progression command applied twice");
                    spent += expectedPrice;
                }
            }

            Assert.True(player.SkillPoints == 0 && player.Money == 1000000 - spent && player.ProgressionRevision == 40, "gold or point conservation failed");
            Assert.True(!SkillLearning.TryQuote(player, skills[0].Code, out _), "rank 11 accepted");
            var low = registry.CreatePlayer(playerClass, "Level", 10);
            low.RestoreProgress(ExperienceTable.ForLevel(10), 10000, skillPoints: 10);
            Assert.True(low.ApplyProgression(new ProgressionCommand(Guid.NewGuid(), 0, ProgressionAction.LearnSkill,
                SkillCode: skills[0].Code, NpcEntityId: 1)), "initial skill rejected at level 10");
            Assert.True(!SkillLearning.TryQuote(low, skills[0].Code, out _), "rank 2 accepted below level 12");
        }

        return Task.CompletedTask;
    }

    private static async Task DurabilityAsync()
    {
        var classes = new CharacterClassRegistry();
        var starter = CharacterRecordFactory.NewStarter(classes, 1, "Training", 0, PlayerClass.Mechanician, 3) with { Id = 1 };
        var character = classes.CreatePlayer(PlayerClass.Mechanician, starter.Name, 40);
        character.RestoreProgress(ExperienceTable.ForLevel(40), 10000, 5, skillPoints: 40);
        var repository = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, 3, new Vector3(100, 0, 100)));
        var npc = new NpcDefinition(1, "Trainer", "trainer.inx", NpcServices.SkillMaster) { Dialog = ["Welcome"] };
        var empty = WorldData.Empty;
        var holder = new WorldDataHolder
        {
            Current = new WorldData(empty.Items, empty.Monsters, new NpcCatalog([npc]),
                new Dictionary<int, FieldSpawnData> { [3] = FieldSpawnData.Empty with { FixedNpcs = [new FixedNpc(1, 100 * 256, 100 * 256, 1024)] } }, empty.Motions)
        };
        var catalog = new WorldCatalog();
        var world = new WorldServer(holder, catalog);
        var sessions = new SessionRegistry();
        var session = sessions.Create(1, "owner");
        var service = new SessionAppService(sessions, classes, repository, catalog, world, holder);
        Assert.True((await service.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "entry failed");
        var outbox = new WorldOutbox();
        var player = WorldPlayerFactory.Create(session);
        await AdvanceAsync(world, world.JoinAsync(3, player, outbox.Writer, session));
        Assert.True(outbox.Reader.TryRead(out var snapshot), "initial roster missing");
        var placed = snapshot.Entities.Single(entity => entity.Kind == EntityKindView.Npc);
        Assert.True(placed.Position == new Vector3(100, 0, 100) && Math.Abs(placed.Facing - MathF.PI / 2) < 0.001, "SPC fixed-point placement is wrong");
        Assert.True((await AdvanceAsync(world, world.InteractNpcAsync(session, placed.EntityId))).Ok, "nearby NPC interaction rejected");
        var skill = FirstTierSkills.For(PlayerClass.Mechanician)[0];
        var command = new ProgressionCommand(Guid.NewGuid(), 0, ProgressionAction.LearnSkill, SkillCode: skill.Code, NpcEntityId: placed.EntityId);
        player.Teleport(new Vector3(1000, 0, 1000));
        Assert.True(!(await AdvanceAsync(world, service.ApplyProgressionAsync(session.SessionToken, command))).Ok, "remote learning accepted");
        player.Teleport(new Vector3(100, 0, 100));
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.BeforeUpdate = _ =>
        {
            writing.TrySetResult();
            return storage.Task;
        };
        var pending = service.ApplyProgressionAsync(session.SessionToken, command);
        await AdvanceAsync(world, writing.Task);
        Assert.True(!pending.IsCompleted && session.Character!.Skills.RankOf(skill.Code) == 0 && session.Character.Money == 10000, "uncommitted learning leaked");
        Assert.True(!(await service.ApplyInventoryAsync(session.SessionToken,
            new InventoryCommand(Guid.NewGuid(), 0, InventoryAction.Move, Guid.NewGuid(), 0, 0, default))).Ok, "inventory bypassed shared save gate");
        storage.SetResult(false);
        Assert.True(!(await pending).Ok && session.Character!.SkillPoints == 40, "storage failure consumed skill points");
        repository.BeforeUpdate = null;
        Assert.True((await AdvanceAsync(world, service.ApplyProgressionAsync(session.SessionToken, command))).Ok, "retry failed");
        Assert.True(session.Character!.Skills.RankOf(skill.Code) == 1 && session.Character.Money == 9500 && session.Character.SkillPoints == 39, "durable learning mismatch");
        var allocation = new ProgressionCommand(Guid.NewGuid(), 1, ProgressionAction.AllocateAttribute, CharacterAttribute.Strength, 1);
        var oldStrength = session.Character.Attributes.Strength;
        Assert.True((await AdvanceAsync(world, service.ApplyProgressionAsync(session.SessionToken, allocation))).Ok, "attribute allocation failed");
        Assert.True(session.Character.Attributes.Strength == oldStrength + 1 && session.Character.StatPoints == 4, "attribute points not conserved");
        var writes = repository.UpdateCount;
        Assert.True((await AdvanceAsync(world, service.ApplyProgressionAsync(session.SessionToken, command))).Ok && repository.UpdateCount == writes, "replay charged again");
        Assert.True(!(await AdvanceAsync(world, service.ApplyProgressionAsync(session.SessionToken, command with { SkillCode = skill.Code + 1 }))).Ok, "conflicting UUID accepted");
        await AdvanceAsync(world, world.LeaveAsync(3, session.EntityId, outbox.Writer));
        sessions.Remove(session.SessionToken);
        var next = sessions.Create(1, "reconnect");
        Assert.True((await service.EnterWorldAsync(next.SessionToken, starter.Name)).Ok && next.TryBeginPlay(), "reconnect failed");
        await AdvanceAsync(world, world.JoinAsync(3, WorldPlayerFactory.Create(next), new WorldOutbox().Writer, next));
        Assert.True((await AdvanceAsync(world, service.ApplyProgressionAsync(next.SessionToken, command))).Ok && repository.UpdateCount == writes, "reconnect lost replay history");
        Assert.True(next.Character!.Skills.RankOf(skill.Code) == 1 && next.Character.Attributes.Strength == oldStrength + 1, "progression was not persisted");
        var owner = OwnerState.Capture(next.Character);
        Assert.True(owner.Skills.Single().Rank == 1 && ReferenceEquals(owner, OwnerState.Capture(next.Character, owner)), "owner rank projection is stale or uncached");
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
