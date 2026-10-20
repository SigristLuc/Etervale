using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.WorldImport.Parsers;

namespace PristonTale.WorldImport;

/// <summary>
/// Orchestrates the idempotent world-data import. Reads the original CP949 config files, resolves
/// cross-references (drop/shop item codes → item_templates; spawn monster/NPC names → FK rows), and writes
/// the baseline tables in dependency order:
/// <c>item_templates → monsters(+drops) → npcs(+shop) → fields(+spawn children)</c>.
///
/// Idempotency contract: parents upsert by their natural PK via <c>INSERT … ON CONFLICT … DO UPDATE</c>
/// (valid on Postgres and SQLite ≥ 3.35); variadic children are <b>delete-then-reinsert</b> per parent, so
/// a 2nd run produces zero net row changes. The whole import runs in one transaction.
///
/// The production <see cref="IWorldDataRepository"/> is read-only by design, so the WRITE SQL lives here
/// (mirroring DapperCharacterRepository's explicit-column, provider-agnostic style).
///
/// Item DWORDs use the original category and ordinal bits of the binary item code, shared with runtime catalog mapping.
/// </summary>
public sealed class Importer
{
    private readonly IDbConnectionFactory _connections;
    private readonly string _root;
    private readonly string? _assetRoot;
    private readonly List<EntityVariant> _entityVariants = [];

    /// <param name="assetRoot">
    /// Game assets root (the directory containing <c>assets/…</c>-relative INX paths) for the motion-timing
    /// pass (combat-fidelity F2). Null = skip the pass (the motion tables stay empty; the world host keeps
    /// its fallback timing constants).
    /// </param>
    public Importer(IDbConnectionFactory connections, string gameServerRoot, string? assetRoot = null)
    {
        _connections = connections;
        _root = gameServerRoot;
        _assetRoot = assetRoot;
    }

    public static long ItemCodeMask(string code) => LegacyItemCode.TryParse(code, out var value) ? value : 0;

    public async Task<ImportReport> RunAsync(CancellationToken ct = default)
    {
        var report = new ImportReport();
        _entityVariants.Clear();
        _npcNamesByFile.Clear();
        _fieldCodeByBase.Clear();

        // ---- 1. Parse all sources -------------------------------------------------------------------
        var items = ParseItems(report);
        var unmappedCategories = items
            .Where(static item => !LegacyItemCode.TryParse(item.Code, out _))
            .Select(static item => item.Code.Length >= 3 ? item.Code[..3].ToUpperInvariant() : item.Code)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static category => category, StringComparer.Ordinal)
            .ToArray();
        if (unmappedCategories.Length > 0)
        {
            report.Warnings.Add($"Item categories absent from the reference DWORD table are retained with mask 0 and excluded from runtime: {string.Join(", ", unmappedCategories)}.");
        }
        var monsters = ParseMonsters(report);
        var npcs = ParseNpcs(report);
        var fields = ParseFields(report);

        // Code/name resolution sets (the PKs that exist after parse). Parents win by natural PK; on dup
        // names we keep the FIRST (and warn) so FK targets are stable. The item-code map is keyed
        // case-insensitively (.inf drops / .npc shops reference codes in lower-case, e.g. "pm104", but the
        // item PK is stored in the .txt's casing, e.g. "PM104") — resolve to the canonical stored casing
        // so the FK matches.
        var itemCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
            itemCodes[it.Code] = it.Code;

        var monsterNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in monsters)
            monsterNames.Add(m.Name);

        var npcNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in npcs)
            npcNames.Add(n.Name);

        await using var conn = await _connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM world_import_unresolved_refs", transaction: tx, cancellationToken: ct));
        await WriteItemsAsync(conn, tx, items, report, ct);
        await WriteMonstersAsync(conn, tx, monsters, itemCodes, report, ct);
        await WriteNpcsAsync(conn, tx, npcs, itemCodes, report, ct);
        await WriteFieldsAsync(conn, tx, fields, monsterNames, npcNames, report, ct);
        await WriteMotionTimingAsync(conn, tx, monsters, npcs, report, ct);
        await WriteSourceFilesAsync(conn, tx, report, ct);
        await WriteEntityVariantsAsync(conn, tx, report, ct);

        await tx.CommitAsync(ct);
        return report;
    }

    private async Task WriteEntityVariantsAsync(
        DbConnection conn,
        DbTransaction tx,
        ImportReport report,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM world_entity_variants", transaction: tx, cancellationToken: ct));

        foreach (var variant in _entityVariants)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO world_entity_variants (source_path, kind, identity_key, is_catalog_entry, data_json)
                VALUES (@SourcePath, @Kind, @IdentityKey, @IsCatalogEntry, @DataJson)
                """,
                variant,
                tx,
                cancellationToken: ct));
            report.EntityVariants++;
        }
    }

    private EntityVariant CreateVariant(string path, string kind, string identity, bool selected, object value)
    {
        var relativePath = Path.GetRelativePath(_root, path).Replace('\\', '/');
        return new EntityVariant(relativePath, kind, identity, selected, JsonSerializer.Serialize(value));
    }

    private sealed record EntityVariant(
        string SourcePath,
        string Kind,
        string IdentityKey,
        bool IsCatalogEntry,
        string DataJson);

    private static async Task RecordUnresolvedAsync(
        DbConnection conn,
        DbTransaction tx,
        string kind,
        string owner,
        string reference,
        object details,
        ImportReport report,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO world_import_unresolved_refs (kind, owner_key, raw_reference, details_json)
            VALUES (@Kind, @Owner, @Reference, @Details)
            """,
            new
            {
                Kind = kind,
                Owner = owner,
                Reference = reference,
                Details = JsonSerializer.Serialize(details),
            },
            tx,
            cancellationToken: ct));
        report.UnresolvedReferenceRows++;
    }

    private async Task WriteSourceFilesAsync(
        DbConnection conn,
        DbTransaction tx,
        ImportReport report,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM world_source_files", transaction: tx, cancellationToken: ct));

        var paths = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal);

        foreach (var path in paths)
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var relativePath = Path.GetRelativePath(_root, path).Replace('\\', '/');
            var fields = ReadSourceFields(path, bytes);
            var kind = relativePath.Split('/')[0].ToLowerInvariant();

            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO world_source_files (source_path, kind, sha256, content_base64, fields_json, field_count)
                VALUES (@Path, @Kind, @Sha256, @Content, @Fields, @FieldCount)
                """,
                new
                {
                    Path = relativePath,
                    Kind = kind,
                    Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                    Content = Convert.ToBase64String(bytes),
                    Fields = JsonSerializer.Serialize(fields),
                    FieldCount = fields.Count,
                },
                tx,
                cancellationToken: ct));

            report.SourceFiles++;
            report.SourceFields += fields.Count;
        }
    }

    private static IReadOnlyList<SourceField> ReadSourceFields(string path, byte[] bytes)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".txt" or ".inf" or ".inf_" or ".npc" or ".spm" or ".zhoon"))
        {
            return [];
        }

        var text = Cp949.GetString(bytes);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var fields = new List<SourceField>();

        for (var index = 0; index < lines.Length; index++)
        {
            var tokenizer = new Tokenizer(lines[index]);
            var key = tokenizer.NextWord();
            if (!key.StartsWith('*'))
            {
                continue;
            }

            fields.Add(new SourceField(index + 1, key, tokenizer.RemainingValues()));
        }

        return fields;
    }

    private sealed record SourceField(int Line, string Key, IReadOnlyList<string> Values);

    // ---- Motion timing (the INX pass; combat-fidelity F2) ---------------------------------------------

    /// <summary>
    /// Extract each distinct monster and NPC model's INX motion table + the player class body tables (via the
    /// shared <see cref="CharacterClassRegistry"/> visuals) and write <c>motion_models</c> /
    /// <c>motion_slots</c>. Idempotent: models upsert; each table's slots are delete-reinserted. Skipped
    /// (with a warning) when no assets root was supplied. Unresolvable models are reported, not fatal —
    /// the world host falls back to its F1 timing constants for them.
    /// </summary>
    private async Task WriteMotionTimingAsync(
        DbConnection conn,
        DbTransaction tx,
        IReadOnlyList<ParsedMonster> monsters,
        IReadOnlyList<ParsedNpc> npcs,
        ImportReport report, CancellationToken ct)
    {
        if (_assetRoot is null)
        {
            report.Warnings.Add("motion pass skipped: no assets root (pass --assets <dir> to import INX motion timing)");
            return;
        }

        var extractor = new MotionTableExtractor(_assetRoot);

        foreach (var m in monsters)
        {
            if (!extractor.TryExtract(m.Model, out var failure))
            {
                report.MotionUnresolvedModels++;
                report.Warnings.Add($"motion: {m.Name}: {failure}");
            }
        }

        foreach (var npc in npcs)
        {
            if (!extractor.TryExtract(npc.Model, out var failure))
            {
                report.MotionUnresolvedModels++;
                report.Warnings.Add($"motion: NPC {npc.Name}: {failure}");
            }
        }

        // Player class bodies — the same shared registry the client/server compose characters from.
        var registry = new CharacterClassRegistry();
        foreach (var cls in registry.All)
        {
            var body = cls.Definition.DefaultVisuals.Body.InxPath;
            if (!extractor.TryExtract(body, out var failure))
            {
                report.MotionUnresolvedModels++;
                report.Warnings.Add($"motion: class {cls.Name}: {failure}");
            }
        }

        foreach (var (modelKey, tableKey) in extractor.Models)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO motion_models (model_key, motion_table_key) VALUES (@ModelKey, @TableKey)
                ON CONFLICT (model_key) DO UPDATE SET motion_table_key = excluded.motion_table_key;
                """,
                new { ModelKey = modelKey, TableKey = tableKey }, tx, cancellationToken: ct));
            report.MotionModelRows++;
        }

        foreach (var (tableKey, slots) in extractor.Tables)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM motion_slots WHERE motion_table_key = @TableKey",
                new { TableKey = tableKey }, tx, cancellationToken: ct));

            foreach (var s in slots)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO motion_slots (
                        motion_table_key, slot, state, start_frame, end_frame,
                        event_frame0, event_frame1, event_frame2, event_frame3, repeat)
                    VALUES (@TableKey, @Slot, @State, @StartFrame, @EndFrame, @E0, @E1, @E2, @E3, @Repeat)
                    """,
                    new
                    {
                        TableKey = tableKey, s.Slot, s.State, s.StartFrame, s.EndFrame,
                        E0 = s.Event0, E1 = s.Event1, E2 = s.Event2, E3 = s.Event3, s.Repeat,
                    }, tx, cancellationToken: ct));
                report.MotionSlotRows++;
            }

            report.MotionTableRows++;
        }
    }

    // ---- Parsing ------------------------------------------------------------------------------------

    private IReadOnlyList<ParsedItem> ParseItems(ImportReport report)
    {
        var parser = new ItemTxtParser();
        var parsed = new List<(string Path, ParsedItem Item)>();

        foreach (var path in EnumerateFiles("OpenItem", "*.txt"))
        {
            report.ItemFilesParsed++;
            ParsedItem item;
            try
            {
                item = parser.Parse(path);
            }
            catch (Exception ex)
            {
                report.Warnings.Add($"item parse failed {Path.GetFileName(path)}: {ex.Message}");
                continue;
            }

            parsed.Add((path, item));
        }

        var winners = parsed
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Item.Code))
            .GroupBy(static entry => entry.Item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderByDescending(static entry => string.Equals(
                    Path.GetFileNameWithoutExtension(entry.Path),
                    entry.Item.Code,
                    StringComparison.OrdinalIgnoreCase))
                .ThenBy(static entry => entry.Path, StringComparer.Ordinal)
                .First())
            .ToDictionary(static entry => entry.Item.Code, StringComparer.OrdinalIgnoreCase);

        var result = new List<ParsedItem>(winners.Count);
        foreach (var (path, item) in parsed)
        {
            var selected = item.Code.Length > 0
                && winners.TryGetValue(item.Code, out var winner)
                && string.Equals(path, winner.Path, StringComparison.Ordinal);

            _entityVariants.Add(CreateVariant(path, "item", item.Code, selected, item));

            if (string.IsNullOrWhiteSpace(item.Code))
            {
                report.Warnings.Add($"item {Path.GetFileName(path)} has no *코드 — retained as an unkeyed variant");
                continue;
            }

            if (!selected)
            {
                report.Warnings.Add($"item variant '{item.Code}' ({Path.GetFileName(path)}) — retained outside the catalog");
                continue;
            }

            result.Add(item);
        }

        return result;
    }

    private IReadOnlyList<ParsedMonster> ParseMonsters(ImportReport report)
    {
        var parser = new MonsterInfParser();
        var result = new List<ParsedMonster>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in EnumerateFiles("Monster", "*.inf"))
        {
            report.MonsterFilesParsed++;
            ParsedMonster m;
            try { m = parser.Parse(path); }
            catch (Exception ex) { report.Warnings.Add($"monster parse failed {Path.GetFileName(path)}: {ex.Message}"); continue; }
            var selected = !string.IsNullOrWhiteSpace(m.Name) && seen.Add(m.Name);
            _entityVariants.Add(CreateVariant(path, "monster", m.Name, selected, m));

            if (string.IsNullOrWhiteSpace(m.Name))
            {
                report.Warnings.Add($"monster {Path.GetFileName(path)} has no *이름 — retained as an unkeyed variant");
                continue;
            }
            if (!selected)
            {
                report.Warnings.Add($"monster variant '{m.Name}' ({Path.GetFileName(path)}) — retained outside the catalog");
                continue;
            }
            if (string.IsNullOrWhiteSpace(m.DisplayName))
            {
                report.Warnings.Add($"monster '{Path.GetFileName(path)}' has no *Name; using its original identity");
            }
            result.Add(m);
        }
        return result;
    }

    private readonly Dictionary<string, string> _npcNamesByFile = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<ParsedNpc> ParseNpcs(ImportReport report)
    {
        var parser = new NpcParser();
        var result = new List<ParsedNpc>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in EnumerateFiles("NPC", "*.npc"))
        {
            report.NpcFilesParsed++;
            ParsedNpc n;
            try { n = parser.Parse(path); }
            catch (Exception ex) { report.Warnings.Add($"npc parse failed {Path.GetFileName(path)}: {ex.Message}"); continue; }
            var selected = !string.IsNullOrWhiteSpace(n.Name) && seen.Add(n.Name);
            _entityVariants.Add(CreateVariant(path, "npc", n.Name, selected, n));

            if (string.IsNullOrWhiteSpace(n.Name))
            {
                report.Warnings.Add($"npc {Path.GetFileName(path)} has no *이름 — retained as an unkeyed variant");
                continue;
            }
            _npcNamesByFile[Path.GetFileName(path)] = n.Name;
            if (!selected)
            {
                report.Warnings.Add($"npc variant '{n.Name}' ({Path.GetFileName(path)}) — retained outside the catalog");
                continue;
            }
            result.Add(n);
        }
        return result;
    }

    private IReadOnlyList<ParsedField> ParseFields(ImportReport report)
    {
        // Build FieldCode + ASE-base join keys from the authoritative Simulation WorldCatalog (D-fields).
        var catalog = new WorldCatalog();
        var spm = new SpmParser();
        var spp = new SppReader();
        var spc = new SpcReader(source => _npcNamesByFile.GetValueOrDefault(Path.GetFileName(source.Replace('\\', '/'))));
        var result = new List<ParsedField>();

        foreach (var def in catalog.AllFields)
        {
            var aseBase = AseBaseOf(def.StageSmd);
            if (aseBase.Length == 0)
                continue;
            var field = new ParsedField { AseBase = aseBase };

            var spmPath = FindFieldFile(aseBase, "spm");
            var sppPath = FindFieldFile(aseBase, "spp");
            var spcPath = FindFieldFile(aseBase, "spc");

            if (spmPath is not null) spm.Parse(spmPath, field);
            if (sppPath is not null) spp.Read(sppPath, field);
            if (spcPath is not null) spc.Read(spcPath, field);

            // Only persist fields that actually have spawn data (the schema anchors spawn data only).
            if (spmPath is null && sppPath is null && spcPath is null)
                continue;

            // Stash the field code on the DTO via a tuple list — attached at write time.
            result.Add(field);
            _fieldCodeByBase[aseBase] = def.FieldCode;
        }
        return result;
    }

    private readonly Dictionary<string, int> _fieldCodeByBase = new(StringComparer.OrdinalIgnoreCase);

    // ---- Writes (idempotent) ------------------------------------------------------------------------

    private static async Task WriteItemsAsync(
        DbConnection conn, DbTransaction tx, IReadOnlyList<ParsedItem> items, ImportReport report, CancellationToken ct)
    {
        const string upsert = """
            INSERT INTO item_templates (
                code, item_code_mask, name, kind, durability_min, durability_max, weight, price,
                damage_min_low, damage_min_high, damage_max_low, damage_max_high,
                attack_rating_min, attack_rating_max, critical_hit, defence_min, defence_max,
                block_min, block_max, absorb_min, absorb_max, speed_min, speed_max,
                mana_regen_min, mana_regen_max, life_regen_min, life_regen_max, stamina_regen_min, stamina_regen_max,
                add_mana_min, add_mana_max, add_life_min, add_life_max, add_stamina_min, add_stamina_max,
                req_level, req_strength, req_spirit, req_talent, req_dexterity, req_health,
                restore_stamina_min, restore_stamina_max, restore_mana_min, restore_mana_max, restore_life_min, restore_life_max,
                shooting_range, attack_speed, job_mask, unique_item,
                res_bionic_min, res_bionic_max, res_earth_min, res_earth_max, res_fire_min, res_fire_max,
                res_ice_min, res_ice_max, res_lighting_min, res_lighting_max, res_poison_min, res_poison_max,
                res_water_min, res_water_max, res_wind_min, res_wind_max,
                max_stack, potion_space, magic_mastery)
            VALUES (
                @Code, @Mask, @Name, @Kind, @DurMin, @DurMax, @Weight, @Price,
                @DmgMinLow, @DmgMinHigh, @DmgMaxLow, @DmgMaxHigh,
                @ArMin, @ArMax, @Crit, @DefMin, @DefMax,
                @BlockMin, @BlockMax, @AbsorbMin, @AbsorbMax, @SpeedMin, @SpeedMax,
                @ManaRegMin, @ManaRegMax, @LifeRegMin, @LifeRegMax, @StaRegMin, @StaRegMax,
                @AddManaMin, @AddManaMax, @AddLifeMin, @AddLifeMax, @AddStaMin, @AddStaMax,
                @ReqLevel, @ReqStr, @ReqSpi, @ReqTal, @ReqDex, @ReqHea,
                @ResStaMin, @ResStaMax, @ResManaMin, @ResManaMax, @ResLifeMin, @ResLifeMax,
                @ShootRange, @AtkSpeed, @JobMask, @Unique,
                @RB0min, @RB0max, @RB1min, @RB1max, @RB2min, @RB2max, @RB3min, @RB3max,
                @RB4min, @RB4max, @RB5min, @RB5max, @RB6min, @RB6max, @RB7min, @RB7max,
                @MaxStack, @PotionSpace, @MagicMastery)
            ON CONFLICT (code) DO UPDATE SET
                item_code_mask=excluded.item_code_mask, name=excluded.name, kind=excluded.kind,
                durability_min=excluded.durability_min, durability_max=excluded.durability_max,
                weight=excluded.weight, price=excluded.price,
                damage_min_low=excluded.damage_min_low, damage_min_high=excluded.damage_min_high,
                damage_max_low=excluded.damage_max_low, damage_max_high=excluded.damage_max_high,
                attack_rating_min=excluded.attack_rating_min, attack_rating_max=excluded.attack_rating_max,
                critical_hit=excluded.critical_hit, defence_min=excluded.defence_min, defence_max=excluded.defence_max,
                block_min=excluded.block_min, block_max=excluded.block_max,
                absorb_min=excluded.absorb_min, absorb_max=excluded.absorb_max,
                speed_min=excluded.speed_min, speed_max=excluded.speed_max,
                mana_regen_min=excluded.mana_regen_min, mana_regen_max=excluded.mana_regen_max,
                life_regen_min=excluded.life_regen_min, life_regen_max=excluded.life_regen_max,
                stamina_regen_min=excluded.stamina_regen_min, stamina_regen_max=excluded.stamina_regen_max,
                add_mana_min=excluded.add_mana_min, add_mana_max=excluded.add_mana_max,
                add_life_min=excluded.add_life_min, add_life_max=excluded.add_life_max,
                add_stamina_min=excluded.add_stamina_min, add_stamina_max=excluded.add_stamina_max,
                req_level=excluded.req_level, req_strength=excluded.req_strength, req_spirit=excluded.req_spirit,
                req_talent=excluded.req_talent, req_dexterity=excluded.req_dexterity, req_health=excluded.req_health,
                restore_stamina_min=excluded.restore_stamina_min, restore_stamina_max=excluded.restore_stamina_max,
                restore_mana_min=excluded.restore_mana_min, restore_mana_max=excluded.restore_mana_max,
                restore_life_min=excluded.restore_life_min, restore_life_max=excluded.restore_life_max,
                shooting_range=excluded.shooting_range, attack_speed=excluded.attack_speed,
                job_mask=excluded.job_mask, unique_item=excluded.unique_item,
                res_bionic_min=excluded.res_bionic_min, res_bionic_max=excluded.res_bionic_max,
                res_earth_min=excluded.res_earth_min, res_earth_max=excluded.res_earth_max,
                res_fire_min=excluded.res_fire_min, res_fire_max=excluded.res_fire_max,
                res_ice_min=excluded.res_ice_min, res_ice_max=excluded.res_ice_max,
                res_lighting_min=excluded.res_lighting_min, res_lighting_max=excluded.res_lighting_max,
                res_poison_min=excluded.res_poison_min, res_poison_max=excluded.res_poison_max,
                res_water_min=excluded.res_water_min, res_water_max=excluded.res_water_max,
                res_wind_min=excluded.res_wind_min, res_wind_max=excluded.res_wind_max,
                max_stack=excluded.max_stack, potion_space=excluded.potion_space,
                magic_mastery=excluded.magic_mastery;
            """;

        foreach (var it in items)
        {
            await conn.ExecuteAsync(new CommandDefinition(upsert, new
            {
                it.Code, Mask = ItemCodeMask(it.Code), it.Name, Kind = (int)it.Kind,
                DurMin = it.DurabilityMin, DurMax = it.DurabilityMax, it.Weight, it.Price,
                DmgMinLow = it.DamageMinLow, DmgMinHigh = it.DamageMinHigh,
                DmgMaxLow = it.DamageMaxLow, DmgMaxHigh = it.DamageMaxHigh,
                ArMin = it.AttackRatingMin, ArMax = it.AttackRatingMax, Crit = it.CriticalHit,
                DefMin = it.DefenceMin, DefMax = it.DefenceMax,
                it.BlockMin, it.BlockMax, it.AbsorbMin, it.AbsorbMax, it.SpeedMin, it.SpeedMax,
                ManaRegMin = it.ManaRegenMin, ManaRegMax = it.ManaRegenMax,
                LifeRegMin = it.LifeRegenMin, LifeRegMax = it.LifeRegenMax,
                StaRegMin = it.StaminaRegenMin, StaRegMax = it.StaminaRegenMax,
                AddManaMin = it.AddManaMin, AddManaMax = it.AddManaMax,
                AddLifeMin = it.AddLifeMin, AddLifeMax = it.AddLifeMax,
                AddStaMin = it.AddStaminaMin, AddStaMax = it.AddStaminaMax,
                ReqLevel = it.ReqLevel, ReqStr = it.ReqStrength, ReqSpi = it.ReqSpirit,
                ReqTal = it.ReqTalent, ReqDex = it.ReqDexterity, ReqHea = it.ReqHealth,
                ResStaMin = it.RestoreStaminaMin, ResStaMax = it.RestoreStaminaMax,
                ResManaMin = it.RestoreManaMin, ResManaMax = it.RestoreManaMax,
                ResLifeMin = it.RestoreLifeMin, ResLifeMax = it.RestoreLifeMax,
                ShootRange = it.ShootingRange, AtkSpeed = it.AttackSpeed,
                JobMask = it.JobMask, Unique = it.UniqueItem,
                RB0min = it.ResMin[0], RB0max = it.ResMax[0], RB1min = it.ResMin[1], RB1max = it.ResMax[1],
                RB2min = it.ResMin[2], RB2max = it.ResMax[2], RB3min = it.ResMin[3], RB3max = it.ResMax[3],
                RB4min = it.ResMin[4], RB4max = it.ResMax[4], RB5min = it.ResMin[5], RB5max = it.ResMax[5],
                RB6min = it.ResMin[6], RB6max = it.ResMax[6], RB7min = it.ResMin[7], RB7max = it.ResMax[7],
                it.MaxStack, it.PotionSpace, it.MagicMastery,
            }, tx, cancellationToken: ct));
            report.ItemRows++;
        }

        // Children: delete-reload per item.
        foreach (var it in items)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM item_template_special WHERE item_code = @Code", new { it.Code }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM item_template_job_random WHERE item_code = @Code", new { it.Code }, tx, cancellationToken: ct));

            foreach (var (bonusCode, v0, v1) in it.Specials)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO item_template_special (item_code, bonus_code, value0, value1) VALUES (@Code,@B,@V0,@V1)",
                    new { it.Code, B = bonusCode, V0 = v0, V1 = v1 }, tx, cancellationToken: ct));
                report.ItemSpecialRows++;
            }
            var ord = 0;
            foreach (var bit in it.JobRandom)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO item_template_job_random (item_code, ordinal, job_bit_code) VALUES (@Code,@O,@J)",
                    new { it.Code, O = ord++, J = bit }, tx, cancellationToken: ct));
                report.ItemJobRandomRows++;
            }
        }
    }

    private static async Task WriteMonstersAsync(
        DbConnection conn, DbTransaction tx, IReadOnlyList<ParsedMonster> monsters,
        IReadOnlyDictionary<string, string> itemCodes, ImportReport report, CancellationToken ct)
    {
        const string upsert = """
            INSERT INTO monsters (
                name, model, level, attack_min, attack_max, attack_speed, attack_range, accuracy,
                defence, absorb_pct, block_pct, hp, sight, skill_damage_min, skill_damage_max,
                skill_distance, skill_range, skill_rating_pct, skill_curse_code, move_speed, move_range,
                iq, group_min, group_max, exp, active_hour, nature, brood, class,
                res_bionic, res_earth, res_fire, res_ice, res_lighting, res_poison, res_water, res_wind,
                stun_pct, display_name, sound_code, model_scale)
            VALUES (
                @Name, @Model, @Level, @AtkMin, @AtkMax, @AtkSpeed, @AtkRange, @Accuracy,
                @Defence, @Absorb, @Block, @Hp, @Sight, @SkDmgMin, @SkDmgMax,
                @SkDist, @SkRange, @SkRating, @SkCurse, @MoveSpeed, @MoveRange,
                @Iq, @GroupMin, @GroupMax, @Exp, @ActiveHour, @Nature, @Brood, @Class,
                @R0, @R1, @R2, @R3, @R4, @R5, @R6, @R7,
                @StunPct, @DisplayName, @SoundCode, @ModelScale)
            ON CONFLICT (name) DO UPDATE SET
                model=excluded.model, level=excluded.level, attack_min=excluded.attack_min, attack_max=excluded.attack_max,
                attack_speed=excluded.attack_speed, attack_range=excluded.attack_range, accuracy=excluded.accuracy,
                defence=excluded.defence, absorb_pct=excluded.absorb_pct, block_pct=excluded.block_pct, hp=excluded.hp,
                sight=excluded.sight, skill_damage_min=excluded.skill_damage_min, skill_damage_max=excluded.skill_damage_max,
                skill_distance=excluded.skill_distance, skill_range=excluded.skill_range,
                skill_rating_pct=excluded.skill_rating_pct, skill_curse_code=excluded.skill_curse_code,
                move_speed=excluded.move_speed, move_range=excluded.move_range, iq=excluded.iq,
                group_min=excluded.group_min, group_max=excluded.group_max, exp=excluded.exp,
                active_hour=excluded.active_hour, nature=excluded.nature, brood=excluded.brood, class=excluded.class,
                res_bionic=excluded.res_bionic, res_earth=excluded.res_earth, res_fire=excluded.res_fire,
                res_ice=excluded.res_ice, res_lighting=excluded.res_lighting, res_poison=excluded.res_poison,
                res_water=excluded.res_water, res_wind=excluded.res_wind, stun_pct=excluded.stun_pct,
                display_name=excluded.display_name, sound_code=excluded.sound_code,
                model_scale=excluded.model_scale;
            """;

        foreach (var m in monsters)
        {
            await conn.ExecuteAsync(new CommandDefinition(upsert, new
            {
                m.Name, m.Model, m.Level, AtkMin = m.AttackMin, AtkMax = m.AttackMax,
                AtkSpeed = m.AttackSpeed, AtkRange = m.AttackRange, m.Accuracy,
                m.Defence, Absorb = m.AbsorbPct, Block = m.BlockPct, m.Hp, m.Sight,
                SkDmgMin = m.SkillDamageMin, SkDmgMax = m.SkillDamageMax,
                SkDist = m.SkillDistance, SkRange = m.SkillRange, SkRating = m.SkillRatingPct, SkCurse = m.SkillCurseCode,
                MoveSpeed = m.MoveSpeed, MoveRange = m.MoveRange, m.Iq,
                GroupMin = m.GroupMin, GroupMax = m.GroupMax, m.Exp, m.ActiveHour,
                Nature = m.Nature, Brood = m.Brood, Class = m.Class,
                R0 = m.Resistances[0], R1 = m.Resistances[1], R2 = m.Resistances[2], R3 = m.Resistances[3],
                R4 = m.Resistances[4], R5 = m.Resistances[5], R6 = m.Resistances[6], R7 = m.Resistances[7],
                m.StunPct, m.DisplayName, m.SoundCode, m.ModelScale,
            }, tx, cancellationToken: ct));
            report.MonsterRows++;
        }

        foreach (var m in monsters)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM monster_drop_items WHERE monster_name = @Name", new { m.Name }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM monster_drops_plus WHERE monster_name = @Name", new { m.Name }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM monster_drops WHERE monster_name = @Name", new { m.Name }, tx, cancellationToken: ct));

            if (m.AllPlayersSee || m.MaxItemsPerKill != 0 || m.DropItems.Count > 0 || m.DropsPlus.Count > 0)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO monster_drops (monster_name, max_items_per_kill, all_players_see) VALUES (@Name,@Max,@All)",
                    new { m.Name, Max = m.MaxItemsPerKill, All = m.AllPlayersSee }, tx, cancellationToken: ct));
                report.MonsterDropConfigRows++;
            }

            foreach (var d in m.DropItems)
            {
                string? itemCode = d.ItemCode;
                if (itemCode is not null)
                {
                    if (itemCodes.TryGetValue(itemCode, out var canon))
                    {
                        itemCode = canon;
                        report.DropItemCodesResolved++;
                    }
                    else
                    {
                        await RecordUnresolvedAsync(
                            conn, tx, "monster_drop", m.Name, itemCode,
                            new { Kind = d.Kind, d.Weight, d.GoldMin, d.GoldMax }, report, ct);
                        report.DropItemCodesDangling++;
                        itemCode = null;
                    }
                }
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO monster_drop_items (monster_name, kind, weight, item_code, gold_min, gold_max) " +
                    "VALUES (@Name,@Kind,@Weight,@Code,@GMin,@GMax)",
                    new { m.Name, Kind = (int)d.Kind, d.Weight, Code = itemCode, GMin = d.GoldMin, GMax = d.GoldMax },
                    tx, cancellationToken: ct));
                report.MonsterDropItemRows++;
            }

            foreach (var p in m.DropsPlus)
            {
                string? itemCode = p.ItemCode;
                if (itemCode is not null)
                {
                    if (itemCodes.TryGetValue(itemCode, out var canon))
                    {
                        itemCode = canon;
                        report.DropItemCodesResolved++;
                    }
                    else
                    {
                        await RecordUnresolvedAsync(
                            conn, tx, "monster_bonus_drop", m.Name, itemCode,
                            new { p.Weight }, report, ct);
                        report.DropItemCodesDangling++;
                        itemCode = null;
                    }
                }
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO monster_drops_plus (monster_name, weight, item_code) VALUES (@Name,@Weight,@Code)",
                    new { m.Name, p.Weight, Code = itemCode }, tx, cancellationToken: ct));
                report.MonsterDropPlusRows++;
            }
        }
    }

    private static async Task WriteNpcsAsync(
        DbConnection conn, DbTransaction tx, IReadOnlyList<ParsedNpc> npcs,
        IReadOnlyDictionary<string, string> itemCodes, ImportReport report, CancellationToken ct)
    {
        const string upsert = """
            INSERT INTO npcs (
                name, model, linked_file, svc_warehouse, svc_shop, svc_skill_master, svc_job_change,
                svc_teleport, svc_item_mix, svc_item_aging, svc_smelting, svc_manufacture, svc_mixture_reset,
                svc_clan, svc_bless_castle, svc_quest, svc_event_booth, svc_collect_money, svc_give_money,
                job_change_code, teleport_code, quest_code, quest_param, dialog, sound_code, move_range, model_scale)
            VALUES (
                @Name, @Model, @Linked, @Warehouse, @Shop, @SkillMaster, @JobChange,
                @Teleport, @ItemMix, @ItemAging, @Smelting, @Manufacture, @MixtureReset,
                @Clan, @BlessCastle, @Quest, @EventBooth, @CollectMoney, @GiveMoney,
                @JobChangeCode, @TeleportCode, @QuestCode, @QuestParam, @Dialog, @SoundCode, @MoveRange, @ModelScale)
            ON CONFLICT (name) DO UPDATE SET
                model=excluded.model, linked_file=excluded.linked_file,
                svc_warehouse=excluded.svc_warehouse, svc_shop=excluded.svc_shop,
                svc_skill_master=excluded.svc_skill_master, svc_job_change=excluded.svc_job_change,
                svc_teleport=excluded.svc_teleport, svc_item_mix=excluded.svc_item_mix,
                svc_item_aging=excluded.svc_item_aging, svc_smelting=excluded.svc_smelting,
                svc_manufacture=excluded.svc_manufacture, svc_mixture_reset=excluded.svc_mixture_reset,
                svc_clan=excluded.svc_clan, svc_bless_castle=excluded.svc_bless_castle,
                svc_quest=excluded.svc_quest, svc_event_booth=excluded.svc_event_booth,
                svc_collect_money=excluded.svc_collect_money, svc_give_money=excluded.svc_give_money,
                job_change_code=excluded.job_change_code, teleport_code=excluded.teleport_code,
                quest_code=excluded.quest_code, quest_param=excluded.quest_param, dialog=excluded.dialog,
                sound_code=excluded.sound_code, move_range=excluded.move_range,
                model_scale=excluded.model_scale;
            """;

        foreach (var n in npcs)
        {
            // Bind dialog as JsonText so the registered per-provider Dapper handler emits jsonb on Postgres
            // (NpgsqlDbType.Jsonb) and TEXT on SQLite — a plain string param sends text and PG rejects it
            // against the jsonb column ("column dialog is of type jsonb but expression is of type text").
            var dialogJson = new JsonText(System.Text.Json.JsonSerializer.Serialize(n.Dialog));
            await conn.ExecuteAsync(new CommandDefinition(upsert, new
            {
                n.Name, n.Model, Linked = n.LinkedFile,
                Warehouse = n.SvcWarehouse, Shop = n.SvcShop, SkillMaster = n.SvcSkillMaster,
                JobChange = n.SvcJobChange, Teleport = n.SvcTeleport, ItemMix = n.SvcItemMix,
                ItemAging = n.SvcItemAging, Smelting = n.SvcSmelting, Manufacture = n.SvcManufacture,
                MixtureReset = n.SvcMixtureReset, Clan = n.SvcClan, BlessCastle = n.SvcBlessCastle,
                Quest = n.SvcQuest, EventBooth = n.SvcEventBooth, CollectMoney = n.SvcCollectMoney,
                GiveMoney = n.SvcGiveMoney,
                JobChangeCode = n.JobChangeCode, TeleportCode = n.TeleportCode,
                n.MoveRange, n.ModelScale,
                QuestCode = n.QuestCode, QuestParam = n.QuestParam, Dialog = dialogJson, n.SoundCode,
            }, tx, cancellationToken: ct));
            report.NpcRows++;
        }

        foreach (var n in npcs)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM npc_shop_stock WHERE npc_name = @Name", new { n.Name }, tx, cancellationToken: ct));
            foreach (var (tab, ordinal, code) in n.Shop)
            {
                if (!itemCodes.TryGetValue(code, out var canon))
                {
                    report.ShopItemCodesDangling++;
                    await RecordUnresolvedAsync(
                        conn, tx, "npc_shop", n.Name, code,
                        new { Tab = tab, Ordinal = ordinal }, report, ct);
                    continue;
                }
                report.ShopItemCodesResolved++;
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO npc_shop_stock (npc_name, tab, ordinal, item_code) VALUES (@Name,@Tab,@Ord,@Code)",
                    new { n.Name, Tab = tab, Ord = ordinal, Code = canon }, tx, cancellationToken: ct));
                report.NpcShopRows++;
            }
        }
    }

    private async Task WriteFieldsAsync(
        DbConnection conn, DbTransaction tx, IReadOnlyList<ParsedField> fields,
        HashSet<string> monsterNames, HashSet<string> npcNames, ImportReport report, CancellationToken ct)
    {
        const string upsert = """
            INSERT INTO fields (field_code, ase_base, max_concurrent_monsters, max_per_spawn_point,
                spawn_interval, per_point_cooldown_ms)
            VALUES (@Code, @Base, @MaxConc, @MaxPer, @Interval, @Cooldown)
            ON CONFLICT (field_code) DO UPDATE SET
                ase_base=excluded.ase_base, max_concurrent_monsters=excluded.max_concurrent_monsters,
                max_per_spawn_point=excluded.max_per_spawn_point, spawn_interval=excluded.spawn_interval,
                per_point_cooldown_ms=excluded.per_point_cooldown_ms;
            """;

        foreach (var f in fields)
        {
            var code = _fieldCodeByBase[f.AseBase];
            await conn.ExecuteAsync(new CommandDefinition(upsert, new
            {
                Code = code, Base = f.AseBase, MaxConc = f.MaxConcurrentMonsters,
                MaxPer = f.MaxPerSpawnPoint, Interval = f.SpawnInterval, Cooldown = f.PerPointCooldownMs,
            }, tx, cancellationToken: ct));
            report.FieldRows++;

            // Children: delete-reload per field.
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM field_fixed_npcs WHERE field_code = @Code", new { Code = code }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM field_boss_spawns WHERE field_code = @Code", new { Code = code }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM field_monster_spawns WHERE field_code = @Code", new { Code = code }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM field_spawn_points WHERE field_code = @Code", new { Code = code }, tx, cancellationToken: ct));

            foreach (var (index, state, x, z) in f.SpawnPoints)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO field_spawn_points (field_code, point_index, state, x, z) VALUES (@Code,@Idx,@State,@X,@Z)",
                    new { Code = code, Idx = index, State = state, X = x, Z = z }, tx, cancellationToken: ct));
                report.SpawnPointRows++;
            }

            foreach (var (monster, weight) in f.MonsterSpawns)
            {
                if (!monsterNames.Contains(monster))
                {
                    report.SpawnMonsterRefsDangling++;
                    report.Warnings.Add($"field '{f.AseBase}' spawn references unknown monster '{monster}'");
                    await RecordUnresolvedAsync(
                        conn, tx, "field_monster_spawn", f.AseBase, monster,
                        new { f.AseBase, Weight = weight }, report, ct);
                    continue;
                }
                report.SpawnMonsterRefsResolved++;
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO field_monster_spawns (field_code, monster_name, weight) VALUES (@Code,@Mon,@W)",
                    new { Code = code, Mon = monster, W = weight }, tx, cancellationToken: ct));
                report.MonsterSpawnRows++;
            }

            foreach (var boss in f.Bosses)
            {
                if (!monsterNames.Contains(boss.MasterName))
                {
                    report.BossMonsterRefsDangling++;
                    report.Warnings.Add($"field '{f.AseBase}' boss references unknown master '{boss.MasterName}'");
                    await RecordUnresolvedAsync(
                        conn, tx, "field_boss", f.AseBase, boss.MasterName,
                        new { f.AseBase, boss.SlaveName, boss.SlaveCount, boss.Hours }, report, ct);
                    continue;
                }
                report.BossMonsterRefsResolved++;
                string? escort = boss.SlaveName;
                if (escort is not null && !monsterNames.Contains(escort))
                {
                    await RecordUnresolvedAsync(
                        conn, tx, "field_boss_escort", f.AseBase, escort,
                        new { f.AseBase, boss.MasterName, boss.SlaveCount, boss.Hours }, report, ct);
                    escort = null;
                }
                var hoursCsv = string.Join(',', boss.Hours);
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO field_boss_spawns (field_code, boss_monster_name, escort_monster_name, escort_count, spawn_hours) " +
                    "VALUES (@Code,@Boss,@Escort,@Count,@Hours)",
                    new { Code = code, Boss = boss.MasterName, Escort = escort, Count = boss.SlaveCount, Hours = hoursCsv },
                    tx, cancellationToken: ct));
                report.BossSpawnRows++;
            }

            foreach (var placement in f.FixedNpcs)
            {
                var npcName = placement.NpcName;
                if (!npcNames.Contains(npcName))
                {
                    report.FixedNpcRefsDangling++;
                    await RecordUnresolvedAsync(
                        conn, tx, "field_fixed_npc", f.AseBase, npcName,
                        new { f.AseBase, placement.X, placement.Z, placement.Angle, placement.Y }, report, ct);
                    continue;
                }
                report.FixedNpcRefsResolved++;
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO field_fixed_npcs (field_code, npc_name, x, z, angle, y) VALUES (@Code,@Npc,@X,@Z,@A,@Y)",
                    new { Code = code, Npc = npcName, X = placement.X, Z = placement.Z, A = placement.Angle, Y = placement.Y }, tx, cancellationToken: ct));
                report.FixedNpcRows++;
            }
        }
    }

    // ---- File helpers -------------------------------------------------------------------------------

    // Glob case-insensitively: the original data mixes extension casing (e.g. NPC has both ".npc" and
    // ".NPC"; Monster has a couple of ".INF"). On a case-sensitive filesystem a plain "*.npc" pattern
    // silently drops the upper-case files, so we always match case-insensitively.
    private static readonly EnumerationOptions CaseInsensitive =
        new() { MatchCasing = MatchCasing.CaseInsensitive };

    private IEnumerable<string> EnumerateFiles(string subfolder, string pattern)
    {
        var dir = ResolveDir(subfolder);
        return dir is null
            ? []
            : Directory.EnumerateFiles(dir, pattern, CaseInsensitive).OrderBy(static p => p, StringComparer.Ordinal);
    }

    // Resolve a subfolder case-insensitively under the GameServer root.
    private string? ResolveDir(string name)
    {
        var direct = Path.Combine(_root, name);
        if (Directory.Exists(direct))
            return direct;
        foreach (var d in Directory.EnumerateDirectories(_root))
            if (string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase))
                return d;
        return null;
    }

    // The field .spm/.spp/.spc are named "<aseBase>.<ext>" or "<aseBase>.ase.<ext>" (case-insensitive).
    private string? FindFieldFile(string aseBase, string ext)
    {
        var dir = ResolveDir("Field");
        if (dir is null)
            return null;
        foreach (var f in Directory.EnumerateFiles(dir, "*." + ext, CaseInsensitive))
        {
            var fileBase = Path.GetFileNameWithoutExtension(f); // strips .spm/.spp/.spc
            // strip a trailing ".ase" if present (the files are "<base>.ase.<ext>").
            if (fileBase.EndsWith(".ase", StringComparison.OrdinalIgnoreCase))
                fileBase = fileBase[..^4];
            if (string.Equals(fileBase, aseBase, StringComparison.OrdinalIgnoreCase))
                return f;
        }
        return null;
    }

    // Derive the ASE base name from a FieldDef.StageSmd path like "Field\\desert\\De-1.smd" → "De-1".
    private static string AseBaseOf(string stageSmd)
    {
        if (string.IsNullOrEmpty(stageSmd))
            return "";
        var norm = stageSmd.Replace('\\', '/');
        var name = norm[(norm.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
