using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;
using PristonTale.WorldImport;
using PristonTale.Assets;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Tests;

internal static class WorldImportTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("world import follows linked data, retains variants and unresolved references", ImportAsync);
        yield return ("every key of the monster, item and NPC files reaches the world data, and unknown keys are reported", EveryKeyAsync);
        yield return ("set bonuses are read from the comment that states them", SetBonusAsync);
    }

    private static Task SetBonusAsync()
    {
        Assert.True(WorldImport.Parsers.SetBonusParser.TryParse("체력+400", out var life) && life.Life == 400, "a life bonus was not read");
        Assert.True(WorldImport.Parsers.SetBonusParser.TryParse("흡수력, 블럭율+2.0", out var guard) && guard.Absorb == 2f && guard.Block == 2f,
            "a value shared by two terms was not read");
        Assert.True(WorldImport.Parsers.SetBonusParser.TryParse("체, 근, 기, 재생+2.0", out var regen) && regen.LifeRegen == 2f && regen.StaminaRegen == 2f
            && regen.ManaRegen == 2f && regen.Life == 0, "abbreviated regeneration was not read");
        Assert.True(WorldImport.Parsers.SetBonusParser.TryParse("체력+200, 흡수력+20, 근기생 재생+30", out var full) && full.Life == 200 && full.Absorb == 20f
            && full.LifeRegen == 30f && full.ManaRegen == 30f && full.StaminaRegen == 30f, "a mixed bonus was not read");
        Assert.True(!WorldImport.Parsers.SetBonusParser.TryParse("행운+3", out _) && !WorldImport.Parsers.SetBonusParser.TryParse("", out _), "an unknown bonus was accepted");
        return Task.CompletedTask;
    }

    private static async Task EveryKeyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-world-keys-{Guid.NewGuid():N}");
        var source = Path.Combine(directory, "GameServer");
        Directory.CreateDirectory(Path.Combine(source, "OpenItem"));
        Directory.CreateDirectory(Path.Combine(source, "Monster"));
        Directory.CreateDirectory(Path.Combine(source, "NPC"));
        await WriteCp949Async(Path.Combine(source, "OpenItem", "WA130.txt"), Lines("*코드 WA130", "*이름 \"Verus Axe\"", "*E_NAME \"Verus Axe\"",
            "*B_NAME \"Machado\"", "*J_NAME \"x\"", "*유니크색상 220 80 0 0 9 1.5", "*이펙트설정 1", "*발생제한 5", "//////// 세트 효과 ////////",
            "// 체력+400", "*세트아이템 1 2"));
        await WriteCp949Async(Path.Combine(source, "OpenItem", "_Uwa130_Rage.txt"),
            Lines("*코드 WA130", "*이름 \"Rage\"", "*유니크", "*유니크색상 0 180 255 0 9"));
        await WriteCp949Async(Path.Combine(source, "Monster", "Wolverine.inf"), Lines("*이름 \"Wolverine\"", "*E_NAME \"Bronze Wolverine\"",
            "*속성 적", "*크기 중대형", "*매직 -5", "*이동타입 2", "*특수공격률 20", "*골드 8 15 80", "*보스",
            @"*예비모델 ""char\monster\Wolf\Wolf-b.ini""", "*이벤트코드 100", "*이벤트정보 1", "*언데드 유", "*수수께끼 1"));
        await WriteCp949Async(Path.Combine(source, "NPC", "Guard.npc"),
            Lines("*이름 \"Guard\"", "*모양파일 \"guard.ini\"", "*속성 적", "*레벨 100", "*크기 대형", "*출현간격 1 2"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var report = await new Importer(provider.GetRequiredService<IDbConnectionFactory>(), source).RunAsync();
            Assert.True(report.Warnings.Any(static warning => warning.Contains("unknown key monster *수수께끼", StringComparison.Ordinal))
                && !report.Warnings.Any(static warning => warning.Contains("unknown key", StringComparison.Ordinal) && !warning.Contains("*수수께끼", StringComparison.Ordinal)),
                "unknown keys were not reported, or known keys were: " + string.Join(" | ", report.Warnings.Where(static w => w.Contains("unknown", StringComparison.Ordinal))));

            var repository = provider.GetRequiredService<IWorldDataRepository>();
            var itemRecords = await repository.GetAllItemTemplatesAsync();
            var items = WorldDataFactory.ToItemDefinitions(itemRecords);
            var axe = items.Single();
            Assert.True(axe.Glow == new ItemGlow(220, 80, 0, 0, 9, 1.5f) && axe.DisplayEffect == 1 && axe.DailyDropLimit == 5 && axe.SetCode == 1,
                "the glow, effect setting, daily limit or set of an item was lost");
            var uniques = WorldDataFactory.ToNamedUniqueDefinitions(itemRecords);
            Assert.True(uniques.Single().Name == "Rage" && uniques.Single().UniqueVariant == 1 && uniques.Single().Code == axe.Code
                && uniques.Single().SavedUnique == ItemDefinition.NamedUniqueBase + 1, "the named unique was not kept as a variant");
            var set = WorldDataFactory.ToItemSets(await repository.GetAllItemSetsAsync()).Single();
            Assert.True(set.SetCode == 1 && set.Pieces == 2 && set.Life == 400, "the set bonus was not imported");

            var monsters = WorldDataFactory.ToMonsterDefinitions(await repository.GetAllMonstersAsync()).Definitions;
            var monster = monsters[0];
            Assert.True(monsters.Count == 2 && monster.EventFormCode == monsters[1].Code && monsters[1].Class == Simulation.World.Monsters.MonsterClass.Normal
                && monsters[1].ModelInxPath.EndsWith("Wolf-b.inx", StringComparison.OrdinalIgnoreCase), "the event form of a monster was not built");
            Assert.True(monster.Name == "Bronze Wolverine" && monster.IsHostile && monster.ShadowSizeLevel == 2 && monster.MagicResistance == -5
                && monster.MoveType == 2 && monster.SpecialAttackPercent == 20 && monster.DeathGold == new Simulation.World.Monsters.MonsterRange(8, 15)
                && monster.DeathGoldChance == 80 && monster.IsBoss && monster.EventModelInxPath.EndsWith("Wolf-b.inx", StringComparison.OrdinalIgnoreCase)
                && monster.EventCode == 100 && monster.EventInfo == 1 && monster.Brood == Simulation.World.Monsters.MonsterBrood.Undead,
                "a monster key was not imported");

            var npc = WorldDataFactory.ToNpcDefinitions(await repository.GetAllNpcsAsync()).Definitions.Single();
            Assert.True(npc.Hostile && npc.Level == 100 && npc.ShadowSizeLevel == 3 && npc.AppearSlot == 1 && npc.AppearCycle == 2
                && npc.AppearsFor(3) && !npc.AppearsFor(4), "an NPC key was not imported");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task ImportAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-world-import-{Guid.NewGuid():N}");
        var source = Path.Combine(directory, "GameServer");
        var itemDirectory = Path.Combine(source, "OpenItem");
        var nameDirectory = Path.Combine(itemDirectory, "name");
        var monsterDirectory = Path.Combine(source, "Monster");
        var npcDirectory = Path.Combine(source, "NPC");
        Directory.CreateDirectory(nameDirectory);
        Directory.CreateDirectory(monsterDirectory);
        Directory.CreateDirectory(Path.Combine(npcDirectory, "name"));

        await WriteCp949Async(Path.Combine(itemDirectory, "2_WA101.txt"), "*코드 WA101\n*이름 \"Variant Axe\"\n");
        await WriteCp949Async(Path.Combine(itemDirectory, "WA101.txt"), "*코드 WA101\n*이름 \"Original Axe\"\n*연결파일 name\\WA101.zhoon\n");
        await WriteCp949Async(Path.Combine(itemDirectory, "BI319.txt"), "*코드 BI319\n*이름 \"Unmapped Blue Stone\"\n");
        await WriteCp949Async(Path.Combine(nameDirectory, "wa101.ZHOON"),
            "*NAME \"Linked Axe\"\n*최대수량 20\n*보유공간 3\n*마법기술숙련도 1.5\n");
        await WriteCp949Async(Path.Combine(monsterDirectory, "Wolf.inf"),
            "*이름 \"Wolf\"\n*Name \"WOLF\"\n*화면보정 -3 30\n*모델 \"wolf.ini\"\n*효과음 WOLVERLIN\n*모델크기 1.4\n*이동속도 12\n*아이템 100 ZZ999\n*아이템 100 BI319\n");
        await WriteCp949Async(Path.Combine(npcDirectory, "Merchant.npc"),
            "*이름 \"상인\"\n*모델 \"merchant.ini\"\n*화면보정 0 70\n*효과음 WOLVERLIN\n*무기판매 WA101 BI319 ZZ999\n*연결파일 name\\missing.zhoon\n");
        // A variant with the same authored name whose linked text supplies the localized name.
        await WriteCp949Async(Path.Combine(npcDirectory, "Merchant2.npc"),
            "*이름 \"상인\"\n*모델 \"merchant.ini\"\n*Name \"Mr Lim\"\n*연결파일 name\\merchant.zhoon\n");
        await WriteCp949Async(Path.Combine(npcDirectory, "name", "merchant.zhoon"), "*B_NAME \"Travelling Merchant\"\n");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));

        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var connections = provider.GetRequiredService<IDbConnectionFactory>();
            var importer = new Importer(connections, source);
            var first = await importer.RunAsync();
            var second = await importer.RunAsync();

            Assert.True(first.ItemRows == 2 && second.ItemRows == 2, "the base items were not selected consistently");
            Assert.True(first.SourceFiles == 8 && first.EntityVariants == 6, "an authored file or variant was omitted");
            Assert.True(first.UnresolvedReferenceRows == 2 && second.UnresolvedReferenceRows == 2,
                "unresolved references were dropped or duplicated");

            await using var connection = await connections.OpenAsync();
            var item = await connection.QuerySingleAsync<(string Name, long Mask, int MaxStack, int PotionSpace, float MagicMastery)>(
                "SELECT name, item_code_mask, max_stack, potion_space, magic_mastery FROM item_templates WHERE code = 'WA101'");
            Assert.True(item.Name == "Linked Axe", "linked display name was not imported");
            Assert.True(item.Mask == ItemCodes.Parse("WA101"), "item code differs from its key");
            Assert.True(await connection.ExecuteScalarAsync<long>(
                "SELECT item_code_mask FROM item_templates WHERE code = 'BI319'") == ItemCodes.Parse("BI319"),
                "an item of a family without other items has no code");
            Assert.True(item.MaxStack == 20 && item.PotionSpace == 3 && Math.Abs(item.MagicMastery - 1.5f) < 0.001f,
                "linked item properties were not imported");

            var monster = await connection.QuerySingleAsync<(int SoundCode, float ModelScale, int MoveSpeed, int SizeLevel)>(
                "SELECT sound_code, model_scale, move_speed, size_level FROM monsters WHERE name = 'Wolf'");
            Assert.True(monster.SizeLevel == 0, "a monster without a size key does not cast the small shadow");
            Assert.True(monster.SoundCode == CharacterSoundCodes.Wolverine, "monster sound code was not imported");
            Assert.True(Math.Abs(monster.ModelScale - 1.4f) < 0.001f && monster.MoveSpeed == 12,
                "monster scale or movement was not imported");
            Assert.True(await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM world_import_unresolved_refs WHERE raw_reference = 'ZZ999'") == 2,
                "unresolved source references were not preserved");
            Assert.True(await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM world_entity_variants WHERE kind = 'item' AND is_catalog_entry = 0") == 1,
                "the duplicate item definition was lost");
            var merchantJson = await connection.ExecuteScalarAsync<string>(
                "SELECT data_json FROM world_entity_variants WHERE kind = 'npc' AND source_path LIKE '%Merchant.npc'");
            Assert.True(merchantJson is not null && merchantJson.Contains("\"Item3\":\"WA101\"", StringComparison.Ordinal),
                "the preserved NPC definition lost its shop stock");
            Assert.True(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") == 0,
                "world import broke foreign keys");

            var repository = provider.GetRequiredService<IWorldDataRepository>();
            var items = WorldDataFactory.ToItemDefinitions(await repository.GetAllItemTemplatesAsync());
            var monsters = WorldDataFactory.ToMonsterDefinitions(await repository.GetAllMonstersAsync()).Definitions;
            var monstersWithCatalog = WorldDataFactory.ToMonsterDefinitions(
                await repository.GetAllMonstersAsync(),
                items.ToDictionary(static item => item.CatalogKey, static item => item.Code, StringComparer.OrdinalIgnoreCase)).Definitions;
            var npcs = WorldDataFactory.ToNpcDefinitions(await repository.GetAllNpcsAsync()).Definitions;
            Assert.True(items.Count == 2 && items.Single(static item => item.CatalogKey == "WA101").MaxStack == 20,
                "runtime item catalog lost the imported stack size or an item family");
            Assert.True(monsters.Single().SoundCode == CharacterSoundCodes.Wolverine,
                "runtime monster catalog lost the imported sound code");
            Assert.True(Math.Abs(monsters.Single().ModelScale - 1.4f) < 0.001f,
                "runtime monster catalog lost the imported model scale");
            Assert.True(monstersWithCatalog.Single().Drops.MainEntries.Where(static entry => entry.Kind == PristonTale.Simulation.World.Loot.DropKind.Item)
                    .Select(static entry => entry.ItemCode).SequenceEqual([ItemCodes.Parse("BI319")]),
                "an unknown authored item leaked into the runtime drop table");
            Assert.True(npcs.Single().Shop.WeaponItemCodes.SequenceEqual([ItemCodes.Parse("WA101"), ItemCodes.Parse("BI319")]),
                "runtime NPC shop uses a different item identity from the catalog");
            Assert.True(npcs.Single().Label == "Travelling Merchant" && npcs.Single().PortraitOffsetZ == 70,
                "the NPC lost the localized name of its variant or its portrait correction");
            Assert.True(monsters.Single().PortraitOffsetY == -3 && monsters.Single().PortraitOffsetZ == 30,
                "the monster portrait correction was not imported");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private static Task WriteCp949Async(string path, string contents)
    {
        return File.WriteAllBytesAsync(path, Cp949.Encoding.GetBytes(contents));
    }
}
