using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;
using PristonTale.WorldImport;

namespace PristonTale.Tests;

internal static class WorldImportTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("world import follows linked data, retains variants and unresolved references", ImportAsync);
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
        Directory.CreateDirectory(npcDirectory);

        await WriteCp949Async(Path.Combine(itemDirectory, "2_WA101.txt"), "*코드 WA101\n*이름 \"Variant Axe\"\n");
        await WriteCp949Async(Path.Combine(itemDirectory, "WA101.txt"), "*코드 WA101\n*이름 \"Original Axe\"\n*연결파일 name\\WA101.zhoon\n");
        await WriteCp949Async(Path.Combine(itemDirectory, "BI319.txt"), "*코드 BI319\n*이름 \"Unmapped Blue Stone\"\n");
        await WriteCp949Async(Path.Combine(nameDirectory, "wa101.ZHOON"),
            "*NAME \"Linked Axe\"\n*최대수량 20\n*보유공간 3\n*마법기술숙련도 1.5\n");
        await WriteCp949Async(Path.Combine(monsterDirectory, "Wolf.inf"),
            "*이름 \"Wolf\"\n*Name \"WOLF\"\n*모델 \"wolf.ini\"\n*효과음 WOLVERLIN\n*모델크기 1.4\n*이동속도 12\n*아이템 100 ZZ999\n*아이템 100 BI319\n");
        await WriteCp949Async(Path.Combine(npcDirectory, "Merchant.npc"),
            "*이름 \"Merchant\"\n*모델 \"merchant.ini\"\n*효과음 WOLVERLIN\n*무기판매 WA101 BI319 ZZ999\n");

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
            Assert.True(first.SourceFiles == 6 && first.EntityVariants == 5, "an authored file or variant was omitted");
            Assert.True(first.UnresolvedReferenceRows == 2 && second.UnresolvedReferenceRows == 2,
                "unresolved references were dropped or duplicated");

            await using var connection = await connections.OpenAsync();
            var item = await connection.QuerySingleAsync<(string Name, long Mask, int MaxStack, int PotionSpace, float MagicMastery)>(
                "SELECT name, item_code_mask, max_stack, potion_space, magic_mastery FROM item_templates WHERE code = 'WA101'");
            Assert.True(item.Name == "Linked Axe", "linked display name was not imported");
            Assert.True(item.Mask == LegacyItemCode.Parse("WA101"), "item mask differs from the original DWORD");
            Assert.True(await connection.ExecuteScalarAsync<long>(
                "SELECT item_code_mask FROM item_templates WHERE code = 'BI319'") == 0,
                "an item absent from the original DWORD table was given an invented code");
            Assert.True(item.MaxStack == 20 && item.PotionSpace == 3 && Math.Abs(item.MagicMastery - 1.5f) < 0.001f,
                "linked item properties were not imported");

            var monster = await connection.QuerySingleAsync<(int SoundCode, float ModelScale, int MoveSpeed)>(
                "SELECT sound_code, model_scale, move_speed FROM monsters WHERE name = 'Wolf'");
            Assert.True(monster.SoundCode == LegacyNpcSoundCode.Wolverine, "monster sound code was not imported");
            Assert.True(Math.Abs(monster.ModelScale - 1.4f) < 0.001f && monster.MoveSpeed == 12,
                "monster scale or movement was not imported");
            Assert.True(await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM world_import_unresolved_refs WHERE raw_reference = 'ZZ999'") == 2,
                "unresolved source references were not preserved");
            Assert.True(await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM world_entity_variants WHERE kind = 'item' AND is_catalog_entry = 0") == 1,
                "the duplicate item definition was lost");
            Assert.True(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") == 0,
                "world import broke foreign keys");

            var repository = provider.GetRequiredService<IWorldDataRepository>();
            var items = WorldDataFactory.ToItemDefinitions(await repository.GetAllItemTemplatesAsync());
            var monsters = WorldDataFactory.ToMonsterDefinitions(await repository.GetAllMonstersAsync()).Definitions;
            var monstersWithCatalog = WorldDataFactory.ToMonsterDefinitions(
                await repository.GetAllMonstersAsync(),
                items.ToDictionary(static item => item.CatalogKey, static item => item.Code, StringComparer.OrdinalIgnoreCase)).Definitions;
            var npcs = WorldDataFactory.ToNpcDefinitions(await repository.GetAllNpcsAsync()).Definitions;
            Assert.True(items.Single().MaxStack == 20, "runtime item catalog lost the imported stack size");
            Assert.True(monsters.Single().SoundCode == LegacyNpcSoundCode.Wolverine,
                "runtime monster catalog lost the imported sound code");
            Assert.True(Math.Abs(monsters.Single().ModelScale - 1.4f) < 0.001f,
                "runtime monster catalog lost the imported model scale");
            Assert.True(monstersWithCatalog.Single().Drops.MainEntries.All(static entry => entry.Kind != PristonTale.Simulation.World.Loot.DropKind.Item),
                "unmapped authored item leaked into the runtime drop table");
            Assert.True(npcs.Single().Shop.WeaponItemCodes.Single() == LegacyItemCode.Parse("WA101"),
                "runtime NPC shop uses a different item identity from the catalog");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task WriteCp949Async(string path, string contents)
    {
        return File.WriteAllBytesAsync(path, Cp949.Encoding.GetBytes(contents));
    }
}
