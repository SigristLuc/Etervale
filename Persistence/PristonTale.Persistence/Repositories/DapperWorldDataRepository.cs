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
            """
            SELECT
                code, item_code_mask, name, kind, durability_min, durability_max, weight, price, damage_min_low,
                damage_min_high, damage_max_low, damage_max_high, attack_rating_min, attack_rating_max,
                critical_hit, defence_min, defence_max, block_min, block_max, absorb_min, absorb_max, speed_min,
                speed_max, mana_regen_min, mana_regen_max, life_regen_min, life_regen_max, stamina_regen_min,
                stamina_regen_max, add_mana_min, add_mana_max, add_life_min, add_life_max, add_stamina_min,
                add_stamina_max, req_level, req_strength, req_spirit, req_talent, req_dexterity, req_health,
                restore_stamina_min, restore_stamina_max, restore_mana_min, restore_mana_max, restore_life_min,
                restore_life_max, shooting_range, attack_speed, job_mask, unique_item, res_bionic_min,
                res_bionic_max, res_earth_min, res_earth_max, res_fire_min, res_fire_max, res_ice_min, res_ice_max,
                res_lighting_min, res_lighting_max, res_poison_min, res_poison_max, res_water_min, res_water_max,
                res_wind_min, res_wind_max, max_stack, potion_space, magic_mastery, grid_width, grid_height,
                glow_r, glow_g, glow_b, glow_a, glow_blink, glow_scale, display_effect, daily_drop_limit, set_code,
                set_pieces
            FROM item_templates
            """, cancellationToken: ct))).ToList();
        if (items.Count == 0)
            return items;

        var specials = (await connection.QueryAsync<ItemTemplateSpecialRecord>(new CommandDefinition(
                "SELECT id, item_code, bonus_code, value0, value1 FROM item_template_special ORDER BY id", cancellationToken: ct)))
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

    public async Task<IReadOnlyList<ItemSetRecord>> GetAllItemSetsAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return (await connection.QueryAsync<ItemSetRecord>(new CommandDefinition(
            "SELECT set_code, pieces, life, absorb, block, life_regen, mana_regen, stamina_regen, description FROM item_sets ORDER BY set_code",
            cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<MonsterRecord>> GetAllMonstersAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);

        var monsters = (await connection.QueryAsync<MonsterRecord>(new CommandDefinition(
            """
            SELECT
                id, key, display_name, model, level, attack_min, attack_max, attack_speed, attack_range, accuracy, defence,
                absorb_pct, block_pct, hp, sight, skill_damage_min, skill_damage_max, skill_distance, skill_range,
                skill_rating_pct, skill_curse_code, move_speed, move_range, iq, group_min, group_max, exp,
                active_hour, nature, brood, class, res_bionic, res_earth, res_fire, res_ice, res_lighting,
                res_poison, res_water, res_wind, stun_pct, sound_code, model_scale,
                portrait_offset_y, portrait_offset_z, quest_code, event_item, potion_count, potion_percent,
                hostile, size_level, res_magic, move_type, special_attack_pct, gold_min, gold_max, gold_pct,
                event_model, event_code, event_info
            FROM monsters ORDER BY id
            """, cancellationToken: ct))).ToList();
        if (monsters.Count == 0)
            return monsters;

        var config = (await connection.QueryAsync<MonsterDropConfigRecord>(new CommandDefinition(
                "SELECT monster_id, max_items_per_kill, all_players_see FROM monster_drops", cancellationToken: ct)))
            .ToDictionary(static c => c.MonsterId);
        var dropItems = (await connection.QueryAsync<MonsterDropItemRecord>(new CommandDefinition(
                "SELECT id, monster_id, kind, weight, item_code, gold_min, gold_max FROM monster_drop_items ORDER BY id",
                cancellationToken: ct)))
            .ToLookup(static d => d.MonsterId);
        var dropsPlus = (await connection.QueryAsync<MonsterDropPlusRecord>(new CommandDefinition(
                "SELECT id, monster_id, weight, item_code FROM monster_drops_plus ORDER BY id", cancellationToken: ct)))
            .ToLookup(static d => d.MonsterId);

        return monsters
            .Select(m => m with
            {
                DropConfig = config.GetValueOrDefault(m.Id),
                DropItems = dropItems[m.Id].ToList(),
                DropsPlus = dropsPlus[m.Id].ToList(),
            })
            .ToList();
    }

    public async Task<IReadOnlyList<NpcRecord>> GetAllNpcsAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);

        var npcs = (await connection.QueryAsync<NpcRecord>(new CommandDefinition(
            """
            SELECT id, key, display_name, model, linked_file, dialog_key, sound_code, move_range, model_scale,
                   portrait_offset_y, portrait_offset_z, hostile, level, size_level, appear_slot, appear_cycle
            FROM npcs ORDER BY id
            """, cancellationToken: ct))).ToList();
        if (npcs.Count == 0)
            return npcs;

        var stock = (await connection.QueryAsync<NpcShopStockRecord>(new CommandDefinition(
                "SELECT id, npc_id, tab, ordinal, item_code FROM npc_shop_stock ORDER BY tab, ordinal",
                cancellationToken: ct)))
            .ToLookup(static s => s.NpcId);
        var services = (await connection.QueryAsync<NpcServiceRecord>(new CommandDefinition(
                "SELECT id, npc_id, ordinal, kind, value, value2, text FROM npc_services ORDER BY npc_id, ordinal",
                cancellationToken: ct)))
            .ToLookup(static s => s.NpcId);
        var dialog = (await connection.QueryAsync<NpcDialogLineRecord>(new CommandDefinition(
                "SELECT npc_id, source_key, ordinal, text FROM npc_dialog_lines ORDER BY npc_id, source_key, ordinal",
                cancellationToken: ct)))
            .ToLookup(static line => (line.NpcId, line.SourceKey));

        return npcs
            .Select(n => n with
            {
                ShopStock = stock[n.Id].ToList(),
                Services = services[n.Id].ToList(),
                Dialog = dialog[(n.Id, n.DialogKey)].Select(static line => line.Text).ToList(),
            })
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
                "SELECT id, field_code, monster_id, weight FROM field_monster_spawns ORDER BY id", cancellationToken: ct)))
            .ToLookup(static s => s.FieldCode);
        var bosses = (await connection.QueryAsync<FieldBossSpawnRecord>(new CommandDefinition(
                """
                SELECT id, field_code, boss_monster_id, escort_monster_id, escort_count, spawn_hours
                FROM field_boss_spawns ORDER BY id
                """, cancellationToken: ct)))
            .ToLookup(static b => b.FieldCode);
        var fixedNpcs = (await connection.QueryAsync<FieldFixedNpcRecord>(new CommandDefinition(
                "SELECT id, field_code, npc_id, x, z, angle, y FROM field_fixed_npcs ORDER BY id", cancellationToken: ct)))
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
                   event_frame0, event_frame1, event_frame2, event_frame3, repeat, key_code
            FROM motion_slots ORDER BY motion_table_key, slot
            """, cancellationToken: ct))).ToList();
    }
}
