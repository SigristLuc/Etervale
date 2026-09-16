using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Sqlite;

namespace PristonTale.Tests;

internal static class MonsterNameTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("baseline preserves monster display names, drops and spawn joins", BaselineAsync);
    }

    private static async Task BaselineAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-monster-names-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));

        try
        {
            await using var provider = services.BuildServiceProvider();
            var migrator = provider.GetRequiredService<IDatabaseMigrator>();
            await migrator.MigrateAsync();

            var connections = provider.GetRequiredService<IDbConnectionFactory>();
            await using (var connection = await connections.OpenAsync())
            {
                await connection.ExecuteAsync("INSERT INTO monsters (name, model, display_name) VALUES (@Name, @Model, @DisplayName)",
                    new { Name = "호피", Model = @"char\monster\Hopy\hopy.INI", DisplayName = "HOPY" });
                await connection.ExecuteAsync("INSERT INTO monster_drops (monster_name) VALUES (@Name)", new { Name = "호피" });
                await connection.ExecuteAsync("INSERT INTO fields (field_code, ase_base) VALUES (3, 'fore-1')");
                await connection.ExecuteAsync("INSERT INTO field_monster_spawns (field_code, monster_name, weight) VALUES (3, @Name, 35)",
                    new { Name = "호피" });
            }

            await migrator.MigrateAsync();

            var repository = provider.GetRequiredService<IWorldDataRepository>();
            var records = await repository.GetAllMonstersAsync();
            var record = records.Single();
            Assert.True(record.Name == "호피" && record.DisplayName == "HOPY", "identity and display name were conflated");
            Assert.True(record.DropConfig?.MonsterName == "호피", "drop association was lost");

            var (definitions, codes) = WorldDataFactory.ToMonsterDefinitions(records);
            Assert.True(definitions.Single().Name == "HOPY", "catalog still exposes the legacy identity");
            var fields = await repository.GetAllFieldSpawnsAsync();
            var spawns = WorldDataFactory.ToFieldSpawnData(fields.Single(), codes, new Dictionary<string, int>());
            Assert.True(spawns.SpawnTable.Single().MonsterCode == definitions.Single().Code, "localized name broke spawn identity");

            await using var check = await connections.OpenAsync();
            Assert.True(await check.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") == 0, "migration broke foreign keys");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
