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
        yield return ("a monster is keyed by its id and English key; drops and spawns join on the id, which is its runtime code", BaselineAsync);
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
                await connection.ExecuteAsync("INSERT INTO monsters (id, key, model, display_name) VALUES (196, @Key, @Model, @DisplayName)",
                    new { Key = "HOPY", Model = @"char\monster\Hopy\hopy.INI", DisplayName = "Hopy" });
                await connection.ExecuteAsync("INSERT INTO monster_drops (monster_id) VALUES (196)");
                await connection.ExecuteAsync("INSERT INTO fields (field_code, ase_base) VALUES (3, 'fore-1')");
                await connection.ExecuteAsync("INSERT INTO field_monster_spawns (field_code, monster_id, weight) VALUES (3, 196, 35)");
            }

            await migrator.MigrateAsync();

            var repository = provider.GetRequiredService<IWorldDataRepository>();
            var records = await repository.GetAllMonstersAsync();
            var record = records.Single();
            Assert.True(record is { Id: 196, Key: "HOPY", DisplayName: "Hopy" }, "the id, key or display name was not read");
            Assert.True(record.DropConfig?.MonsterId == 196, "drop association was lost");

            var definition = WorldDataFactory.ToMonsterDefinitions(records).Single();
            Assert.True(definition is { Code: 196, Key: "HOPY", Name: "Hopy" }, "the runtime code is not the id, or the key or label differs");
            var fields = await repository.GetAllFieldSpawnsAsync();
            var spawns = WorldDataFactory.ToFieldSpawnData(fields.Single());
            Assert.True(spawns.SpawnTable.Single().MonsterCode == 196, "the spawn does not name the monster by its id");

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
