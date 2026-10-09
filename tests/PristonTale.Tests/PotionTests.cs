using System.Numerics;
using System.Threading.Channels;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Effects.Particles;
using PristonTale.Gameplay.Combat;
using PristonTale.Engine3D.Formats;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

internal static class PotionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("potions restore at the EAT event once, clamp resources and cannot revive", TimingAsync);
        yield return ("potion INI animation preserves intro, middle loop and exit fade", AnimationAsync);
        yield return ("potion storage failure preserves stacks; commit and replay consume once", DurabilityAsync);
    }

    private static Task TimingAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Potion");
        character.Life = 1;
        character.Mana = 0;
        character.Stamina = 0;
        var potion = new PotionDefinition(1000, 1000, 1000, 1000, 1000, 1000);
        var random = new CombatRng(1);
        var player = new WorldPlayer(1, Vector3.Zero, character, false, default);
        player.QueuePotion(potion);
        player.BeginSwing(2, Vector3.One, 100, 50);
        Assert.True(!player.BeginPendingPotion(600, 500), "potion interrupted an active swing");
        player.TickAction(100, out _);
        Assert.True(player.BeginPendingPotion(600, 500), "queued potion did not start after attack");
        Assert.True(player.ReportMovement(new Vector3(50, 0, 0), 0f, true, true, 0, 1000) != MovementVerdict.Accepted && player.Position == Vector3.Zero,
            "movement interrupted EAT");
        Assert.True(player.TickPotion(499, random) == PotionResource.None && character.Life == 1, "restored before event");
        Assert.True(player.TickPotion(1, random) == (PotionResource)7, "missing resource effects at event");
        Assert.True(character.Life == character.MaxLife && character.Mana == character.MaxMana
            && character.Stamina == character.MaxStamina, "restoration did not clamp to maxima");
        character.Life = 1;
        Assert.True(player.TickPotion(100, random) == PotionResource.None && character.Life == 1 && !player.HasPendingPotion,
            "potion restored twice or stayed busy");

        player.QueuePotion(potion);
        player.BeginPendingPotion(600, 500);
        player.ApplyDamage(10000);
        Assert.True(player.TickPotion(1000, random) == PotionResource.None && player.IsDead && character.Life == 0
            && !player.HasPendingPotion, "potion revived a dead player");

        var motion = new ActorMotionController();
        Assert.True(motion.ApplyAuthoritative(PristonActorMotion.Eat).Kind == MotionCommandKind.Restart, "EAT was not a one-shot");
        Assert.True(motion.ApplyLocomotion(PristonActorMotion.Run).Kind == MotionCommandKind.None, "locomotion overrode EAT");
        motion.ApplyAuthoritative(PristonActorMotion.Stand);
        Assert.True(!motion.LocomotionFrozen, "EAT completion did not release input");
        return Task.CompletedTask;
    }

    private static Task AnimationAsync()
    {
        var animation = new ParticleAnimation([
            new(0, 20f / 70f, 50f / 255f),
            new(1, 5f / 70f, 1f),
            new(2, 5f / 70f, 1f),
            new(3, 5f / 70f, 225f / 255f),
            new(0, 30f / 70f, 0f)
        ], 0f);
        Assert.True(animation.Sample(0, 1).Alpha == 0f, "intro does not start transparent");
        Assert.True(animation.Sample(21f / 70f, 1).Image == 1, "nonuniform intro duration ignored");
        Assert.True(animation.Sample(36f / 70f, 1).Image == 1, "loop replayed introduction or final fade");
        var fade = animation.Sample(1f + 15f / 70f, 1);
        Assert.True(fade.Image == 0 && Math.Abs(fade.Alpha - 225f / 510f) < 0.001f, "exit fade is incorrect");
        Assert.True(animation.Sample(2, 1).Alpha == 0f, "exit does not become transparent");

        var recipe = new EmitterDefinition { MaxParticles = 30, BurstCount = 30, Animation = animation, PotionMotion = true };
        var oneStep = new ParticleEmitter(recipe, Microsoft.Xna.Framework.Vector3.Zero, 42);
        var manySteps = new ParticleEmitter(recipe, Microsoft.Xna.Framework.Vector3.Zero, 42);
        oneStep.Update(0.5f, Microsoft.Xna.Framework.Vector3.Zero);
        for (var frame = 0; frame < 60; frame++)
        {
            manySteps.Update(0.5f / 60f, Microsoft.Xna.Framework.Vector3.Zero);
        }

        var first = oneStep.GetRenderView().Particles;
        var second = manySteps.GetRenderView().Particles;
        Assert.True(first.Length == 30 && second.Length == 30, "potion particle count differs from original");
        for (var index = 0; index < first.Length; index++)
        {
            Assert.True(Microsoft.Xna.Framework.Vector3.Distance(first[index].Position, second[index].Position) < 0.001f,
                "potion orbit depends on render FPS");
        }

        return Task.CompletedTask;
    }

    private static async Task DurabilityAsync()
    {
        var classes = new CharacterClassRegistry();
        var starter = CharacterRecordFactory.NewStarter(classes, 1, "DurablePotion", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var character = classes.CreatePlayer(PlayerClass.Fighter, starter.Name);
        character.Life = 1;
        var definition = new ItemDefinition(1, "Health potion", ItemKind.Potion, MaxStack: 100)
        {
            CatalogKey = "PL101",
            Potion = new PotionDefinition(10, 10, 0, 0, 0, 0)
        };
        var item = new ItemInstance(definition, 3);
        character.Inventory.TryPlace(item, 0);
        var repository = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, 3, Vector3.Zero));
        var data = WorldData.Empty;
        var holder = new WorldDataHolder
        {
            Current = new WorldData(new ItemCatalog([definition]), data.Monsters, data.Npcs, data.FieldSpawns, data.Motions)
        };
        var catalog = new WorldCatalog();
        var world = new WorldServer(holder, catalog);
        var sessions = new SessionRegistry();
        var session = sessions.Create(1, "owner");
        var service = new SessionAppService(sessions, classes, repository, catalog, world, holder);
        Assert.True((await service.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "entry failed");
        var outbound = Channel.CreateUnbounded<WorldSnapshot>();
        var player = WorldPlayerFactory.Create(session);
        await AdvanceAsync(world, world.JoinAsync(3, player, outbound.Writer, session));
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.BeforeUpdate = _ =>
        {
            writing.TrySetResult();
            return storage.Task;
        };
        var command = new InventoryCommand(Guid.NewGuid(), session.Character!.InventoryRevision, InventoryAction.ConsumePotion,
            item.InstanceId, 0, 1, default);
        var pending = service.ApplyInventoryAsync(session.SessionToken, command);
        await AdvanceAsync(world, writing.Task);
        Assert.True(session.Character.Inventory[0].Count == 3 && session.Character.Life == 1 && !player.HasPendingPotion,
            "pending save consumed/healed through shared item reference");
        storage.SetResult(false);
        Assert.True(!(await pending).Ok && session.Character.Inventory[0].Count == 3, "storage rejection consumed potion");
        repository.BeforeUpdate = null;
        var accepted = service.ApplyInventoryAsync(session.SessionToken, command);
        await AdvanceAsync(world, accepted);
        Assert.True((await accepted).Ok && session.Character.Inventory[0].Count == 2, "commit did not consume exactly one");
        for (uint tick = 0; tick < 30; tick++)
        {
            world.Tick(0.05f, tick);
        }

        Assert.True(session.Character.Life == 11 && !player.HasPendingPotion, "committed potion did not restore exactly once");
        var begin = 0;
        var applied = 0;
        while (outbound.Reader.TryRead(out var snapshot))
        {
            foreach (var effect in snapshot.Events)
            {
                begin += effect.Kind == CombatEventKindView.PotionBegin ? 1 : 0;
                applied += effect.Kind == CombatEventKindView.PotionApplied && effect.Resources == 1 ? 1 : 0;
            }
        }

        Assert.True(begin == 1 && applied == 1, "potion events missing or duplicated");
        var writes = repository.UpdateCount;
        var replay = service.ApplyInventoryAsync(session.SessionToken, command);
        await AdvanceAsync(world, replay);
        Assert.True((await replay).Ok && repository.UpdateCount == writes && session.Character.Life == 11
            && session.Character.Inventory[0].Count == 2 && !player.HasPendingPotion, "retry consumed or healed twice");
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
}
