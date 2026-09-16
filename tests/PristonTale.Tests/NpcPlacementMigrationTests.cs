using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.World.Motion;

namespace PristonTale.Tests;

internal static class NpcPlacementMigrationTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("baseline creates the complete NPC schema and preserves imported data on rerun", BaselineAsync);
    }

    private static async Task BaselineAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-npc-baseline-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));

        try
        {
            await using var provider = services.BuildServiceProvider();
            var migrator = provider.GetRequiredService<IDatabaseMigrator>();
            await migrator.MigrateAsync();

            var connections = provider.GetRequiredService<IDbConnectionFactory>();
            await using var connection = await connections.OpenAsync();
            Assert.True(
                await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"VersionInfo\" WHERE \"Version\" = 13") == 1,
                "the consolidated baseline version was not recorded");
            Assert.True(
                await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('characters') WHERE name IN ('face_index', 'tier', 'hair_index', 'size_level')") == 4,
                "the baseline omitted character appearance columns");

            await connection.ExecuteAsync("INSERT INTO fields (field_code, ase_base) VALUES (3, 'fixture')");
            await connection.ExecuteAsync("INSERT INTO npcs (name, model) VALUES ('Warehouse', 'fixture.inx')");
            await connection.ExecuteAsync(
                "INSERT INTO field_fixed_npcs (field_code, npc_name, x, y, z, angle) VALUES (3, 'Warehouse', 1, 2, 3, 4)");
            await connection.ExecuteAsync(
                "INSERT INTO motion_models (model_key, motion_table_key) VALUES ('char/npc/tn-010/tn-010.inx', 'custom/npc-motion.inx')");
            await connection.ExecuteAsync(
                """
                INSERT INTO motion_slots (
                    motion_table_key, slot, state, start_frame, end_frame,
                    event_frame0, event_frame1, event_frame2, event_frame3, repeat)
                VALUES ('custom/npc-motion.inx', 10, 80, 1, 9, 0, 0, 0, 0, 1)
                """);

            await migrator.MigrateAsync();

            Assert.True(
                await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"VersionInfo\"") == 1,
                "rerunning the baseline created a second version");
            Assert.True(
                await connection.ExecuteScalarAsync<int>("SELECT move_range FROM npcs WHERE name = 'Warehouse'") == 64,
                "the baseline omitted the NPC movement default");
            Assert.True(
                await connection.ExecuteScalarAsync<int>("SELECT sound_code FROM npcs WHERE name = 'Warehouse'") == 0,
                "the baseline omitted the NPC sound-code column");
            Assert.True(
                await connection.ExecuteScalarAsync<int>("SELECT y FROM field_fixed_npcs WHERE npc_name = 'Warehouse'") == 2,
                "rerunning the baseline changed an imported NPC position");
            Assert.True(
                await connection.ExecuteScalarAsync<string>(
                    "SELECT motion_table_key FROM motion_models WHERE model_key = 'char/npc/tn-010/tn-010.inx'") == "custom/npc-motion.inx",
                "rerunning the baseline replaced an imported motion mapping");

            var repository = provider.GetRequiredService<IWorldDataRepository>();
            var catalog = WorldDataFactory.ToMotionTimings(
                await repository.GetAllMotionModelsAsync(),
                await repository.GetAllMotionSlotsAsync());
            Assert.True(
                catalog.TryGet("char\\npc\\TN-010\\TN-010.ini", out var motions),
                "runtime motion catalog did not resolve the imported NPC model");
            Assert.True(motions.TryGetClip(MotionStateCode.Walk, out _), "runtime motion catalog omitted the Walk clip");
            Assert.True(
                await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") == 0,
                "the baseline broke foreign keys");
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
