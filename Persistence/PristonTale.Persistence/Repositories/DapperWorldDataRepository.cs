using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.WorldData;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperWorldDataRepository(IDbConnectionFactory connectionFactory) : IWorldDataRepository
{
    public async Task<IReadOnlyList<ItemTemplateRecord>> GetAllItemTemplatesAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);

        var items = (await connection.QueryAsync<ItemTemplateRecord>(new CommandDefinition(
            "SELECT * FROM item_templates", cancellationToken: ct))).ToList();
        if (items.Count == 0)
            return items;

        var specials = (await connection.QueryAsync<ItemTemplateSpecialRecord>(new CommandDefinition(
                "SELECT id, item_code, bonus_code, value0, value1 FROM item_template_special", cancellationToken: ct)))
            .ToLookup(static s => s.ItemCode);
        var jobRandom = (await connection.QueryAsync<ItemTemplateJobRandomRecord>(new CommandDefinition(
                "SELECT id, item_code, ordinal, job_bit_code FROM item_template_job_random ORDER BY ordinal",
                cancellationToken: ct)))
            .ToLookup(static j => j.ItemCode);

        return items
            .Select(i => i with
            {
                Specials = specials[i.Code].ToList(),
                JobRandom = jobRandom[i.Code].ToList(),
            })
            .ToList();
    }

    public async Task<IReadOnlyList<MonsterRecord>> GetAllMonstersAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);

        // Runtime monster IDs derive from this deterministic order.
        var monsters = (await connection.QueryAsync<MonsterRecord>(new CommandDefinition(
            "SELECT * FROM monsters ORDER BY name", cancellationToken: ct))).ToList();
        if (monsters.Count == 0)
            return monsters;

        var config = (await connection.QueryAsync<MonsterDropConfigRecord>(new CommandDefinition(
                "SELECT monster_name, max_items_per_kill, all_players_see FROM monster_drops", cancellationToken: ct)))
            .ToDictionary(static c => c.MonsterName);
        var dropItems = (await connection.QueryAsync<MonsterDropItemRecord>(new CommandDefinition(
                "SELECT id, monster_name, kind, weight, item_code, gold_min, gold_max FROM monster_drop_items",
                cancellationToken: ct)))
            .ToLookup(static d => d.MonsterName);
        var dropsPlus = (await connection.QueryAsync<MonsterDropPlusRecord>(new CommandDefinition(
                "SELECT id, monster_name, weight, item_code FROM monster_drops_plus", cancellationToken: ct)))
            .ToLookup(static d => d.MonsterName);

        return monsters
            .Select(m => m with
            {
                DropConfig = config.GetValueOrDefault(m.Name),
                DropItems = dropItems[m.Name].ToList(),
                DropsPlus = dropsPlus[m.Name].ToList(),
            })
            .ToList();
    }

    public async Task<IReadOnlyList<NpcRecord>> GetAllNpcsAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);

        // Runtime NPC IDs derive from this deterministic order.
        var npcs = (await connection.QueryAsync<NpcRecord>(new CommandDefinition(
            "SELECT * FROM npcs ORDER BY name", cancellationToken: ct))).ToList();
        if (npcs.Count == 0)
            return npcs;

        var stock = (await connection.QueryAsync<NpcShopStockRecord>(new CommandDefinition(
                "SELECT id, npc_name, tab, ordinal, item_code FROM npc_shop_stock ORDER BY tab, ordinal",
                cancellationToken: ct)))
            .ToLookup(static s => s.NpcName);

        return npcs
            .Select(n => n with { ShopStock = stock[n.Name].ToList() })
            .ToList();
    }

    public async Task<IReadOnlyList<FieldSpawnRecord>> GetAllFieldSpawnsAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);

        var fields = (await connection.QueryAsync<FieldSpawnRecord>(new CommandDefinition(
            """
            SELECT field_code, ase_base, max_concurrent_monsters, max_per_spawn_point,
                   spawn_interval, per_point_cooldown_ms
            FROM fields
            """, cancellationToken: ct))).ToList();
        if (fields.Count == 0)
            return fields;

        var points = (await connection.QueryAsync<FieldSpawnPointRecord>(new CommandDefinition(
                "SELECT id, field_code, point_index, state, x, z FROM field_spawn_points ORDER BY point_index",
                cancellationToken: ct)))
            .ToLookup(static p => p.FieldCode);
        var spawns = (await connection.QueryAsync<FieldMonsterSpawnRecord>(new CommandDefinition(
                "SELECT id, field_code, monster_name, weight FROM field_monster_spawns", cancellationToken: ct)))
            .ToLookup(static s => s.FieldCode);
        var bosses = (await connection.QueryAsync<FieldBossSpawnRecord>(new CommandDefinition(
                """
                SELECT id, field_code, boss_monster_name, escort_monster_name, escort_count, spawn_hours
                FROM field_boss_spawns
                """, cancellationToken: ct)))
            .ToLookup(static b => b.FieldCode);
        var fixedNpcs = (await connection.QueryAsync<FieldFixedNpcRecord>(new CommandDefinition(
                "SELECT id, field_code, npc_name, x, z, angle, y FROM field_fixed_npcs", cancellationToken: ct)))
            .ToLookup(static n => n.FieldCode);

        return fields
            .Select(f => f with
            {
                SpawnPoints = points[f.FieldCode].ToList(),
                MonsterSpawns = spawns[f.FieldCode].ToList(),
                BossSpawns = bosses[f.FieldCode].ToList(),
                FixedNpcs = fixedNpcs[f.FieldCode].ToList(),
            })
            .ToList();
    }

    public async Task<IReadOnlyList<MotionModelRecord>> GetAllMotionModelsAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return (await connection.QueryAsync<MotionModelRecord>(new CommandDefinition(
            "SELECT model_key, motion_table_key FROM motion_models", cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<MotionSlotRecord>> GetAllMotionSlotsAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        // ORDER BY (table, slot): slot order IS the first-clip-per-state selection order the client
        // renderer uses — WorldDataFactory.ToMotionTimings relies on it (grouped tables, ascending slots).
        return (await connection.QueryAsync<MotionSlotRecord>(new CommandDefinition(
            """
            SELECT motion_table_key, slot, state, start_frame, end_frame,
                   event_frame0, event_frame1, event_frame2, event_frame3, repeat
            FROM motion_slots ORDER BY motion_table_key, slot
            """, cancellationToken: ct))).ToList();
    }
}
