using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Tests;

internal static class MonsterCrystalTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("crystal tables draw their monsters by weight, uniformly or from the event table, and pets scale with their owner", TablesAsync);
        yield return ("a crystal calls an owned pet beside its user, consumes itself and plays the return sparkle", SummonAsync);
        yield return ("billing crystals wait for the shared delay; towns, clan crystals, mercenaries and unknown souls refuse", RefusalsAsync);
        yield return ("a crystal pet hunts the nearest wild monster, credits its owner and dies when it expires", PetHuntsAsync);
    }

    private static Task TablesAsync()
    {
        Assert.True(MonsterCrystals.TryGet("gp109", out var mystic) && mystic.Pick(0, false) == " 호피 " && mystic.Pick(99, false) == " 킹호피 "
            && mystic.Pick(39, false) == " 홉고블린 " && mystic.Pick(40, false) == " 디코이 ", "mystic weights");
        Assert.True(mystic.Pick(0, true) == " 짱피 ", "mystic event table");
        Assert.True(MonsterCrystals.TryGet("GP120", out var extreme) && extreme.Pick(34, false) == " 오미크론 " && extreme.Pick(35, false) == " 스파이더 트루퍼 "
            && extreme.Pick(99, false) == " 오메가 " && extreme.SharedDelay, "extreme weights");
        Assert.True(MonsterCrystals.TryGet("GP117", out var skeleton) && skeleton.Pick(1, false) == " 스켈레톤 레인저 " && skeleton.Pick(7, true) == " 스켈레톤 나이트 ",
            "billing crystals draw uniformly in and out of the event");
        Assert.True(MonsterCrystals.TryGet("GP101", out var hopy) && hopy.Pick(5, false) == " 호피 " && !hopy.SharedDelay, "fixed crystal");
        Assert.True(MonsterCrystals.TryGet("GP205", out var cyan) && cyan.Kind == CrystalKind.SoulStone && cyan.Pick(6, false) == " 굴가르의 영혼 ",
            "soul stone table");
        Assert.True(MonsterCrystals.PetLifetimeMs(50) == 340_000f && MonsterCrystals.PetLifetimeMs(100) == 240_000f && MonsterCrystals.PetBonusLife(99) == 297,
            "pet lifetime and life bonus");
        Assert.True(MonsterCrystals.EventActive(1, 12) && !MonsterCrystals.EventActive(1, 11) && MonsterCrystals.EventActive(2, 3)
            && !MonsterCrystals.EventActive(0, 15), "crystal event hours");
        Assert.True(!UsableItemCatalog.IsUsable("GP301") && !UsableItemCatalog.IsUsable("GP123") && UsableItemCatalog.IsUsable("GP122"),
            "crystals without a defined behaviour stay unusable");
        return Task.CompletedTask;
    }

    private static async Task SummonAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(20, [Hopy()], [], "GP101", "GP101");
        var character = harness.Session.Character!;
        var use = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP101")));
        await harness.AdvanceAsync(use);
        Assert.True((await use).Outcome.Ok && character.OwnedItems().Count(item => item.Definition.CatalogKey == "GP101") == 1, "crystal was not consumed once");
        harness.Tick(1);
        Assert.True(harness.World.TryGetOpenField(20, out var field), "field missing");
        var pet = field.MonstersForInspection.Single(monster => monster.OwnerId == harness.Player.EntityId);
        Assert.True(pet.Def.Brood == MonsterBrood.User && pet.Def.Faction == MonsterFaction.Npc && pet.Def.Combat.MaxLife == 150 + 3, "pet is not owned");
        Assert.True(Vector3.Distance(pet.Position, harness.Player.Position) <= 60f, "pet was not called beside its owner");
        Assert.True(harness.ItemEffects().Any(effect => effect.Effect == ItemEffectKind.Return && effect.EntityId == harness.Player.EntityId),
            "return sparkle missing");
        var again = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP101")));
        await harness.AdvanceAsync(again);
        harness.Tick(1);
        Assert.True((await again).Outcome.Ok && field.MonstersForInspection.Count(monster => monster.OwnerId == harness.Player.EntityId) == 2,
            "ordinary crystals have no count limit and no delay");
    }

    private static async Task RefusalsAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(20, [Hopy(), .. Skeletons()], [], "GP117", "GP117", "GP110", "GP114", "GP201");
        var character = harness.Session.Character!;
        var first = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP117")));
        await harness.AdvanceAsync(first);
        Assert.True((await first).Outcome.Ok, "skeleton crystal refused");
        var second = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP117")));
        await harness.AdvanceAsync(second);
        Assert.True((await second).Outcome.Status == ItemUseStatus.Delay && harness.Item("GP117") is not null, "shared delay did not refuse");
        harness.Clock.Now = harness.Clock.Now.AddSeconds(3);
        var third = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP117")));
        await harness.AdvanceAsync(third);
        Assert.True((await third).Outcome.Ok, "the delay outlived three seconds");

        var clan = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP110")));
        await harness.AdvanceAsync(clan);
        Assert.True((await clan).Outcome.Status == ItemUseStatus.ClanOnly && harness.Item("GP110") is not null, "clan crystal without a clan");
        var mercenary = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP114")));
        await harness.AdvanceAsync(mercenary);
        Assert.True((await mercenary).Outcome.Status == ItemUseStatus.CrystalRefused && harness.Item("GP114") is not null, "mercenary without a siege");
        harness.Clock.Now = harness.Clock.Now.AddSeconds(3);
        var soul = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP201")));
        await harness.AdvanceAsync(soul);
        Assert.True((await soul).Outcome.Status == ItemUseStatus.NotUsable && harness.Item("GP201") is not null, "soul stone without its monsters");

        var town = await SpecialItemTests.Harness.CreateAsync(3, [Hopy()], [], "GP101");
        var inTown = town.Use(new ItemUseCommand(Guid.NewGuid(), town.Session.Character!.InventoryRevision, town.Id("GP101")));
        await town.AdvanceAsync(inTown);
        Assert.True((await inTown).Outcome.Status == ItemUseStatus.NotHere && town.Item("GP101") is not null, "crystal worked in town");
    }

    private static Task PetHuntsAsync()
    {
        var wild = SkillScenario.Enemy("Wild", life: 2, sight: 0, level: 1) with { ExpReward = 100 };
        var scenario = SkillScenario.Create(Simulation.Characters.PlayerClass.Fighter, 20, null, null, wild, Hopy());
        Assert.True(scenario.Field.TryFindMonsterKey(" 호피 ", out var hopy), "catalog lacks the crystal monster");
        var spawned = scenario.Field.CallCrystalMonster(scenario.Player, "GP101", " 호피 ");
        Assert.True(spawned is not null, "pet was not called");
        var prey = scenario.Spawn(wild, spawned!.Value + new Vector3(60f, 0f, 0f));
        var experience = scenario.Character.Experience;
        scenario.Run(200);
        Assert.True(!prey.IsAlive && scenario.Character.Experience > experience, "the pet did not hunt for its owner");
        var pet = scenario.Field.MonstersForInspection.Single(monster => monster.OwnerId == scenario.Player.EntityId);
        pet.ExpiresAtMs = 0;
        scenario.Run(2);
        Assert.True(!pet.IsAlive, "an expired pet kept fighting");
        return Task.CompletedTask;
    }

    private static MonsterDefinition Hopy() => SkillScenario.SummonModel("HOPY", 50) with
    {
        Key = " 호피 ",
        Combat = new MonsterCombatStats(4, 400, new MonsterRange(5, 6), 6, 50, 50, 5, 0, 150, ResistanceBlock.Zero)
    };

    private static MonsterDefinition[] Skeletons() =>
    [
        SkillScenario.SummonModel("Skeleton Archer", 51) with { Key = " 스켈레톤 아처 " },
        SkillScenario.SummonModel("Skeleton Ranger", 52) with { Key = " 스켈레톤 레인저 " },
        SkillScenario.SummonModel("Skeleton Warrior", 53) with { Key = " 스켈레톤 워리어 " },
        SkillScenario.SummonModel("Skeleton Knight", 54) with { Key = " 스켈레톤 나이트 " }
    ];
}
