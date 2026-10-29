using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.WorldImport.Parsers;
using PristonTale.Assets;

namespace PristonTale.WorldImport;

// Orchestrates the idempotent world-data import. Reads the original CP949 config files, resolves
// cross-references (drop/shop item codes → item_templates; authored monster/NPC identities → their registry id,
// IdentityRegistry), and writes the baseline tables in dependency order:
// item_templates → monsters(+drops) → npcs(+shop) → fields(+spawn children).
//
// Idempotency contract: parents upsert by their key (item code, monster/NPC id) via INSERT … ON CONFLICT … DO UPDATE
// (valid on Postgres and SQLite ≥ 3.35); variadic children are delete-then-reinsert per parent, so
// a 2nd run produces zero net row changes. The whole import runs in one transaction.
//
// The production IWorldDataRepository is read-only by design, so the WRITE SQL lives here
// (mirroring DapperCharacterRepository's explicit-column, provider-agnostic style).
//
// Item DWORDs use the original category and ordinal bits of the binary item code, shared with runtime catalog mapping.
public sealed class Importer
{
    private readonly IDbConnectionFactory _connections;
    private readonly string _root;
    private readonly string? _assetRoot;
    private readonly List<EntityVariant> _entityVariants = [];
    private IdentityRegistry _monsterIds = null!;
    private IdentityRegistry _npcIds = null!;

    // assetRoot: game assets root (the directory containing assets/…-relative INX paths) for the motion-timing
    // pass. Null = skip the pass (the motion tables stay empty; the world host keeps its fallback timing constants).
    public Importer(IDbConnectionFactory connections, string gameServerRoot, string? assetRoot = null)
    {
        _connections = connections;
        _root = gameServerRoot;
        _assetRoot = assetRoot;
    }

    public static long ItemCodeMask(string code) => ItemCodes.TryParse(code, out var value) ? value : 0;

    public async Task<ImportReport> RunAsync(CancellationToken ct = default)
    {
        var report = new ImportReport();
        _entityVariants.Clear();
        _unknownKeys.Clear();
        _npcNamesByFile.Clear();
        _fieldCodeByBase.Clear();
        _monsterIds = IdentityRegistry.Monsters();
        _npcIds = IdentityRegistry.Npcs();

        // ---- 1. Parse all sources -------------------------------------------------------------------
        var items = ParseItems(report);
        var invalidKeys = items
            .Where(static item => !ItemCodes.TryParse(item.Code, out _))
            .Select(static item => item.Code)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (invalidKeys.Length > 0)
        {
            report.Warnings.Add($"Item keys that are not two letters, a letter or digit and two digits are retained with mask 0 and excluded from runtime: {string.Join(", ", invalidKeys)}.");
        }
        var monsters = ParseMonsters(report);
        var npcs = ParseNpcs(report);
        ReportUnknownKeys(report);
        var fields = ParseFields(report);

        // Code/name resolution sets (the PKs that exist after parse). Parents win by natural PK; on dup
        // names we keep the FIRST (and warn) so FK targets are stable. The item-code map is keyed
        // case-insensitively (.inf drops / .npc shops reference codes in lower-case, e.g. "pm104", but the
        // item PK is stored in the .txt's casing, e.g. "PM104") — resolve to the canonical stored casing
        // so the FK matches.
        var itemCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
            itemCodes[it.Code] = it.Code;

        var monsterIds = monsters.ToDictionary(static m => m.Name, m => _monsterIds.Resolve(m.Name, m.DisplayName), StringComparer.Ordinal);
        var npcIds = npcs.ToDictionary(static n => n.Name, n => _npcIds.Resolve(n.Name, n.DisplayName), StringComparer.Ordinal);
        ReportUnregistered(_monsterIds, report);
        ReportUnregistered(_npcIds, report);

        await using var conn = await _connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM world_import_unresolved_refs", transaction: tx, cancellationToken: ct));
        var footprints = ItemFootprints.Measure(_assetRoot, items.Select(static item => item.Code), report);
        await WriteItemsAsync(conn, tx, items, _questVariants, _namedUniques, footprints, report, ct);
        await WriteItemSetsAsync(conn, tx, items, report, ct);
        await WriteMonstersAsync(conn, tx, monsters, monsterIds, itemCodes, report, ct);
        await WriteNpcsAsync(conn, tx, npcs, npcIds, itemCodes, report, ct);
        await WriteFieldsAsync(conn, tx, fields, monsterIds, npcIds, report, ct);
        await WriteMotionTimingAsync(conn, tx, monsters, npcs, monsterIds, npcIds, report, ct);
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
                new { variant.SourcePath, variant.Kind, IdentityKey = IdentityKeyOf(variant), variant.IsCatalogEntry, variant.DataJson },
                tx,
                cancellationToken: ct));
            report.EntityVariants++;
        }
    }

    // A monster or NPC variant is listed under its English key; an item under its code; an unkeyed file under none.
    private string IdentityKeyOf(EntityVariant variant) => variant.Kind switch
    {
        "monster" => _monsterIds.TryGet(variant.Identity, out var monster) ? monster.Key : "",
        "npc" => _npcIds.TryGet(variant.Identity, out var npc) ? npc.Key : "",
        _ => variant.Identity
    };

    private static void ReportUnregistered(IdentityRegistry registry, ImportReport report)
    {
        foreach (var entry in registry.Added)
        {
            report.Warnings.Add($"Identities/{registry.FileName} lacks '{entry.Key}': imported as id {entry.Id}; add {IdentityRegistry.Line(entry)}");
        }
    }

    // Definitions hold value-tuple lists (item specials, shop stock); tuples keep their values only as fields.
    private static readonly JsonSerializerOptions VariantJson = new() { IncludeFields = true };

    private EntityVariant CreateVariant(string path, string kind, string identity, bool selected, object value)
    {
        var relativePath = Path.GetRelativePath(_root, path).Replace('\\', '/');
        return new EntityVariant(relativePath, kind, identity, selected, JsonSerializer.Serialize(value, VariantJson));
    }

    // Identity: the item code, or the monster's or NPC's authored identity (IdentityKeyOf gives its key).
    private sealed record EntityVariant(
        string SourcePath,
        string Kind,
        string Identity,
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

    // Extract each distinct monster and NPC model's INX motion table + the player class body tables (via the
    // shared CharacterClassRegistry visuals) and write motion_models /
    // motion_slots. Idempotent: models upsert; each table's slots are delete-reinserted. Skipped
    // (with a warning) when no assets root was supplied. Unresolvable models are reported, not fatal —
    // the world host falls back to its default timing constants for them.
    private async Task WriteMotionTimingAsync(
        DbConnection conn,
        DbTransaction tx,
        IReadOnlyList<ParsedMonster> monsters,
        IReadOnlyList<ParsedNpc> npcs,
        IReadOnlyDictionary<string, IdentityRegistry.Entry> monsterIds,
        IReadOnlyDictionary<string, IdentityRegistry.Entry> npcIds,
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
                report.Warnings.Add($"motion: {monsterIds[m.Name].Key}: {failure}");
            }
        }

        foreach (var npc in npcs)
        {
            if (!extractor.TryExtract(npc.Model, out var failure))
            {
                report.MotionUnresolvedModels++;
                report.Warnings.Add($"motion: NPC {npcIds[npc.Name].Key}: {failure}");
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
                        event_frame0, event_frame1, event_frame2, event_frame3, repeat, key_code)
                    VALUES (@TableKey, @Slot, @State, @StartFrame, @EndFrame, @E0, @E1, @E2, @E3, @Repeat, @KeyCode)
                    """,
                    new
                    {
                        TableKey = tableKey, s.Slot, s.State, s.StartFrame, s.EndFrame,
                        E0 = s.Event0, E1 = s.Event1, E2 = s.Event2, E3 = s.Event3, s.Repeat, s.KeyCode,
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
            NoteUnknownKeys("item", path, item.UnknownKeys);
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
                if (item.UniqueItem != NamedUnique)
                {
                    report.Warnings.Add($"item variant '{item.Code}' ({Path.GetFileName(path)}) — retained outside the catalog");
                }

                continue;
            }

            result.Add(item);
        }

        // The quest variant of a code (unique marker 2) is kept beside the regular definition under "<code>#Q".
        _questVariants.Clear();
        foreach (var group in parsed.Where(static entry => entry.Item.Code.Length > 0 && entry.Item.UniqueItem == QuestUnique)
                     .GroupBy(static entry => entry.Item.Code, StringComparer.OrdinalIgnoreCase))
        {
            _questVariants.Add(group.OrderBy(static entry => entry.Path, StringComparer.Ordinal).First().Item);
        }

        // Named uniques (unique marker 1) of a code that lost the selection: "<code>#U<n>", one per distinct name in path order.
        _namedUniques.Clear();
        foreach (var group in parsed.Where(entry => entry.Item.Code.Length > 0 && entry.Item.UniqueItem == NamedUnique
                         && !string.Equals(winners[entry.Item.Code].Path, entry.Path, StringComparison.Ordinal))
                     .GroupBy(static entry => entry.Item.Code, StringComparer.OrdinalIgnoreCase))
        {
            var variant = 0;
            foreach (var entry in group.OrderBy(static entry => entry.Path, StringComparer.Ordinal).DistinctBy(static entry => entry.Item.Name))
            {
                _namedUniques.Add((++variant, entry.Item));
            }
        }

        return result;
    }

    private const long NamedUnique = 1;
    private const string NamedUniqueSuffix = "#U";
    private readonly List<(int Variant, ParsedItem Item)> _namedUniques = [];
    private const long QuestUnique = 2;
    private const string QuestVariantSuffix = "#Q";
    private readonly List<ParsedItem> _questVariants = [];

    // Keys no rule reads, by source kind: how many files carry each and the first one.
    private readonly SortedDictionary<string, (int Files, string First)> _unknownKeys = new(StringComparer.Ordinal);

    private void NoteUnknownKeys(string kind, string path, IEnumerable<string> keys)
    {
        // Comment lines and stray text are not keys.
        foreach (var key in keys.Where(static key => key.StartsWith('*')).Distinct(StringComparer.Ordinal))
        {
            var id = $"{kind} {key}";
            _unknownKeys[id] = _unknownKeys.TryGetValue(id, out var seen) ? (seen.Files + 1, seen.First) : (1, Path.GetFileName(path));
        }
    }

    private void ReportUnknownKeys(ImportReport report)
    {
        foreach (var (id, (files, first)) in _unknownKeys)
        {
            report.Warnings.Add($"unknown key {id}: {files} file(s), first {first} — not imported");
        }
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
            NoteUnknownKeys("monster", path, m.UnknownKeys);
            var selected = !string.IsNullOrWhiteSpace(m.Name) && seen.Add(m.Name);
            _entityVariants.Add(CreateVariant(path, "monster", m.Name, selected, m));

            if (string.IsNullOrWhiteSpace(m.Name))
            {
                report.Warnings.Add($"monster {Path.GetFileName(path)} has no *이름 — retained as an unkeyed variant");
                continue;
            }
            if (!selected)
            {
                report.Warnings.Add($"monster variant '{m.DisplayName}' ({Path.GetFileName(path)}) — retained outside the catalog");
                continue;
            }
            if (string.IsNullOrWhiteSpace(m.DisplayName))
            {
                report.Warnings.Add($"monster {Path.GetFileName(path)} has no English name; its key is shown");
            }
            result.Add(m);
        }
        return result;
    }

    private readonly Dictionary<string, string> _npcNamesByFile = new(StringComparer.OrdinalIgnoreCase);

    // Several files may share an identity name. The catalog keeps the first file a field places, else the first file
    // read, so a placed NPC keeps its own model, dialog and services; the other files remain variants.
    private IReadOnlyList<ParsedNpc> ParseNpcs(ImportReport report)
    {
        var parser = new NpcParser();
        var parsed = new List<(string Path, ParsedNpc Npc)>();
        foreach (var path in EnumerateFiles("NPC", "*.npc"))
        {
            report.NpcFilesParsed++;
            try
            {
                var npc = parser.Parse(path);
                parsed.Add((path, npc));
                NoteUnknownKeys("npc", path, npc.UnknownKeys);
            }
            catch (Exception ex)
            {
                report.Warnings.Add($"npc parse failed {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        var placed = PlacedNpcFiles();
        var chosen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, npc) in parsed.OrderBy(entry => placed.Contains(Path.GetFileName(entry.Path)) ? 0 : 1))
        {
            if (!string.IsNullOrWhiteSpace(npc.Name))
            {
                chosen.TryAdd(npc.Name, path);
            }
        }

        var result = new List<ParsedNpc>();
        foreach (var (path, n) in parsed)
        {
            var selected = !string.IsNullOrWhiteSpace(n.Name) && chosen[n.Name] == path;
            _entityVariants.Add(CreateVariant(path, "npc", n.Name, selected, n));

            if (string.IsNullOrWhiteSpace(n.Name))
            {
                report.Warnings.Add($"npc {Path.GetFileName(path)} has no *이름 — retained as an unkeyed variant");
                continue;
            }

            _npcNamesByFile[Path.GetFileName(path)] = n.Name;
            if (!selected)
            {
                report.Warnings.Add($"npc variant '{n.DisplayName}' ({Path.GetFileName(path)}) — retained outside the catalog");
                continue;
            }

            result.Add(n);
        }

        // Definitions sharing a name are one catalog entry; when the selected file has no localized name, players see
        // the first variant's that has one.
        foreach (var n in result)
        {
            if (n.DisplayName.Length == 0)
            {
                n.DisplayName = parsed.Select(entry => entry.Npc).FirstOrDefault(variant => variant.Name == n.Name && variant.DisplayName.Length > 0)?.DisplayName ?? "";
            }

            if (n.DisplayName.Length == 0)
            {
                report.Warnings.Add($"npc {Path.GetFileName(chosen[n.Name])} has no English name in any definition; its key is shown");
            }
        }

        return result;
    }

    private HashSet<string> PlacedNpcFiles()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in new WorldCatalog().AllFields)
        {
            var aseBase = AseBaseOf(def.StageSmd);
            var spcPath = aseBase.Length == 0 ? null : FindFieldFile(aseBase, "spc");
            if (spcPath is null)
            {
                continue;
            }

            foreach (var source in SpcReader.Sources(spcPath))
            {
                files.Add(Path.GetFileName(source.Replace('\\', '/')));
            }
        }

        return files;
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

    // One row per item set: the pieces worn for the bonus (the most any of its items asks) and the bonus its files state.
    private static async Task WriteItemSetsAsync(DbConnection conn, DbTransaction tx, IReadOnlyList<ParsedItem> items, ImportReport report,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM item_sets", transaction: tx, cancellationToken: ct));
        foreach (var set in items.Where(static item => item.SetCode != 0).GroupBy(static item => item.SetCode).OrderBy(static set => set.Key))
        {
            var texts = set.Select(static item => item.SetBonus).Where(static text => text.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            var text = texts.FirstOrDefault() ?? "";
            if (texts.Count > 1)
            {
                report.Warnings.Add($"item set {set.Key}: its items state different bonuses ({string.Join(" | ", texts)}); the first is used");
            }

            if (!SetBonusParser.TryParse(text, out var bonus))
            {
                report.Warnings.Add($"item set {set.Key}: bonus '{text}' not understood; the set gives nothing");
            }

            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO item_sets (set_code, pieces, life, absorb, block, life_regen, mana_regen, stamina_regen, description)
                VALUES (@SetCode, @Pieces, @Life, @Absorb, @Block, @LifeRegen, @ManaRegen, @StaminaRegen, @Description)
                """,
                new
                {
                    SetCode = set.Key, Pieces = set.Max(static item => item.SetPieces), bonus.Life, bonus.Absorb, bonus.Block, bonus.LifeRegen,
                    bonus.ManaRegen, bonus.StaminaRegen, Description = Describe(bonus),
                }, tx, cancellationToken: ct));
        }
    }

    // The set bonus in English, as the item window would list it ("Life +400, Absorb +2").
    private static string Describe(ParsedSetBonus bonus)
    {
        var terms = new List<string>();
        void Add(string name, float value)
        {
            if (value != 0)
            {
                terms.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{name} +{value:0.##}"));
            }
        }

        Add("Life", bonus.Life);
        Add("Absorb", bonus.Absorb);
        Add("Block", bonus.Block);
        Add("Life Regen", bonus.LifeRegen);
        Add("Mana Regen", bonus.ManaRegen);
        Add("Stamina Regen", bonus.StaminaRegen);
        return string.Join(", ", terms);
    }

    private static async Task WriteItemsAsync(
        DbConnection conn, DbTransaction tx, IReadOnlyList<ParsedItem> items, IReadOnlyList<ParsedItem> questVariants,
        IReadOnlyList<(int Variant, ParsedItem Item)> namedUniques, IReadOnlyDictionary<string, (int Width, int Height)> footprints,
        ImportReport report, CancellationToken ct)
    {
        var rows = items.Select(static it => (Key: it.Code, Item: it))
            .Concat(questVariants.Select(static it => (Key: it.Code + QuestVariantSuffix, Item: it)))
            .Concat(namedUniques.Select(static unique => (Key: unique.Item.Code + NamedUniqueSuffix + unique.Variant, unique.Item)))
            .ToList();
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
                max_stack, potion_space, magic_mastery, grid_width, grid_height, glow_r, glow_g, glow_b, glow_a, glow_blink,
                glow_scale, display_effect, daily_drop_limit, set_code, set_pieces)
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
                @MaxStack, @PotionSpace, @MagicMastery, @GridWidth, @GridHeight, @GlowR, @GlowG, @GlowB, @GlowA, @GlowBlink,
                @GlowScale, @DisplayEffect, @DailyDropLimit, @SetCode, @SetPieces)
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
                magic_mastery=excluded.magic_mastery, grid_width=excluded.grid_width, grid_height=excluded.grid_height,
                glow_r=excluded.glow_r, glow_g=excluded.glow_g, glow_b=excluded.glow_b, glow_a=excluded.glow_a,
                glow_blink=excluded.glow_blink, glow_scale=excluded.glow_scale, display_effect=excluded.display_effect,
                daily_drop_limit=excluded.daily_drop_limit, set_code=excluded.set_code, set_pieces=excluded.set_pieces;
            """;

        foreach (var (key, it) in rows)
        {
            await conn.ExecuteAsync(new CommandDefinition(upsert, new
            {
                Code = key, Mask = ItemCodeMask(it.Code), it.Name, Kind = (int)it.Kind,
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
                GridWidth = footprints.TryGetValue(it.Code, out var footprint) ? footprint.Width : 1,
                GridHeight = footprints.TryGetValue(it.Code, out footprint) ? footprint.Height : 1,
                GlowR = it.UniqueColor[0], GlowG = it.UniqueColor[1], GlowB = it.UniqueColor[2], GlowA = it.UniqueColor[3],
                GlowBlink = it.UniqueBlink, GlowScale = it.UniqueScale, it.DisplayEffect, it.DailyDropLimit, it.SetCode, it.SetPieces,
            }, tx, cancellationToken: ct));
            report.ItemRows++;
        }

        // Children: delete-reload per item.
        foreach (var (key, it) in rows)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM item_template_special WHERE item_code = @Code", new { Code = key }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM item_template_job_random WHERE item_code = @Code", new { Code = key }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM item_labels WHERE item_code = @Code", new { Code = key }, tx, cancellationToken: ct));
            foreach (var (sourceKey, text) in it.Labels.OrderBy(static label => label.Key, StringComparer.Ordinal))
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO item_labels (item_code, source_key, text) VALUES (@Code, @SourceKey, @Text)",
                    new { Code = key, SourceKey = sourceKey, Text = text }, tx, cancellationToken: ct));
                report.LabelRows++;
            }

            foreach (var (bonusCode, v0, v1) in it.Specials)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO item_template_special (item_code, bonus_code, value0, value1) VALUES (@Code,@B,@V0,@V1)",
                    new { Code = key, B = bonusCode, V0 = v0, V1 = v1 }, tx, cancellationToken: ct));
                report.ItemSpecialRows++;
            }
            var ord = 0;
            foreach (var bit in it.JobRandom)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO item_template_job_random (item_code, ordinal, job_bit_code) VALUES (@Code,@O,@J)",
                    new { Code = key, O = ord++, J = bit }, tx, cancellationToken: ct));
                report.ItemJobRandomRows++;
            }
        }
    }

    // A monster or NPC no longer in the source leaves with its children, so a re-keyed identity never meets its old row.
    private static async Task DeleteStaleAsync(DbConnection conn, DbTransaction tx, string table, IEnumerable<IdentityRegistry.Entry> current,
        CancellationToken ct)
    {
        var kept = current.Select(static identity => identity.Id).ToHashSet();
        var stale = (await conn.QueryAsync<int>(new CommandDefinition($"SELECT id FROM {table}", transaction: tx, cancellationToken: ct)))
            .Where(id => !kept.Contains(id))
            .Select(static id => new { Id = id })
            .ToArray();
        await conn.ExecuteAsync(new CommandDefinition($"DELETE FROM {table} WHERE id = @Id", stale, tx, cancellationToken: ct));
    }

    private static async Task WriteMonstersAsync(
        DbConnection conn, DbTransaction tx, IReadOnlyList<ParsedMonster> monsters, IReadOnlyDictionary<string, IdentityRegistry.Entry> monsterIds,
        IReadOnlyDictionary<string, string> itemCodes, ImportReport report, CancellationToken ct)
    {
        const string upsert = """
            INSERT INTO monsters (
                id, key, display_name, model, level, attack_min, attack_max, attack_speed, attack_range, accuracy,
                defence, absorb_pct, block_pct, hp, sight, skill_damage_min, skill_damage_max,
                skill_distance, skill_range, skill_rating_pct, skill_curse_code, move_speed, move_range,
                iq, group_min, group_max, exp, active_hour, nature, brood, class,
                res_bionic, res_earth, res_fire, res_ice, res_lighting, res_poison, res_water, res_wind,
                stun_pct, sound_code, model_scale, portrait_offset_y, portrait_offset_z,
                quest_code, event_item, potion_count, potion_percent, hostile, size_level, res_magic, move_type,
                special_attack_pct, gold_min, gold_max, gold_pct, event_model, event_code, event_info)
            VALUES (
                @Id, @Key, @DisplayName, @Model, @Level, @AtkMin, @AtkMax, @AtkSpeed, @AtkRange, @Accuracy,
                @Defence, @Absorb, @Block, @Life, @Sight, @SkDmgMin, @SkDmgMax,
                @SkDist, @SkRange, @SkRating, @SkCurse, @MoveSpeed, @MoveRange,
                @Iq, @GroupMin, @GroupMax, @Exp, @ActiveHour, @Nature, @Brood, @Class,
                @R0, @R1, @R2, @R3, @R4, @R5, @R6, @R7,
                @StunPct, @SoundCode, @ModelScale, @PortraitOffsetY, @PortraitOffsetZ,
                @QuestCode, @EventItem, @PotionCount, @PotionPercent, @Hostile, @SizeLevel, @MagicResistance, @MoveType,
                @SpecialAttackPct, @GoldMin, @GoldMax, @GoldPct, @EventModel, @EventCode, @EventInfo)
            ON CONFLICT (id) DO UPDATE SET
                key=excluded.key, display_name=excluded.display_name,
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
                sound_code=excluded.sound_code,
                model_scale=excluded.model_scale, portrait_offset_y=excluded.portrait_offset_y,
                portrait_offset_z=excluded.portrait_offset_z, quest_code=excluded.quest_code, event_item=excluded.event_item,
                potion_count=excluded.potion_count, potion_percent=excluded.potion_percent, hostile=excluded.hostile,
                size_level=excluded.size_level, res_magic=excluded.res_magic, move_type=excluded.move_type,
                special_attack_pct=excluded.special_attack_pct, gold_min=excluded.gold_min, gold_max=excluded.gold_max,
                gold_pct=excluded.gold_pct, event_model=excluded.event_model, event_code=excluded.event_code,
                event_info=excluded.event_info;
            """;

        await DeleteStaleAsync(conn, tx, "monsters", monsterIds.Values, ct);
        foreach (var m in monsters)
        {
            var identity = monsterIds[m.Name];
            await conn.ExecuteAsync(new CommandDefinition(upsert, new
            {
                identity.Id, identity.Key, m.DisplayName, m.Model, m.Level, AtkMin = m.AttackMin, AtkMax = m.AttackMax,
                AtkSpeed = m.AttackSpeed, AtkRange = m.AttackRange, m.Accuracy,
                m.Defence, Absorb = m.AbsorbPct, Block = m.BlockPct, m.Life, m.Sight,
                SkDmgMin = m.SkillDamageMin, SkDmgMax = m.SkillDamageMax,
                SkDist = m.SkillDistance, SkRange = m.SkillRange, SkRating = m.SkillRatingPct, SkCurse = m.SkillCurseCode,
                MoveSpeed = m.MoveSpeed, MoveRange = m.MoveRange, m.Iq,
                GroupMin = m.GroupMin, GroupMax = m.GroupMax, m.Exp, m.ActiveHour,
                Nature = m.Nature, Brood = m.Brood, Class = m.Class,
                R0 = m.Resistances[0], R1 = m.Resistances[1], R2 = m.Resistances[2], R3 = m.Resistances[3],
                R4 = m.Resistances[4], R5 = m.Resistances[5], R6 = m.Resistances[6], R7 = m.Resistances[7],
                m.StunPct, m.SoundCode, m.ModelScale, m.PortraitOffsetY, m.PortraitOffsetZ,
                m.QuestCode, EventItem = itemCodes.TryGetValue(m.EventItem, out var eventItem) ? eventItem : m.EventItem, m.PotionCount, m.PotionPercent,
                m.Hostile, m.SizeLevel, m.MagicResistance, m.MoveType, m.SpecialAttackPct, m.GoldMin, m.GoldMax, m.GoldPct,
                m.EventModel, m.EventCode, m.EventInfo,
            }, tx, cancellationToken: ct));
            report.MonsterRows++;
        }

        foreach (var m in monsters)
        {
            var identity = monsterIds[m.Name];
            var owner = new { identity.Id };
            await conn.ExecuteAsync(new CommandDefinition("DELETE FROM monster_labels WHERE monster_id = @Id", owner, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition("DELETE FROM monster_drop_items WHERE monster_id = @Id", owner, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition("DELETE FROM monster_drops_plus WHERE monster_id = @Id", owner, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition("DELETE FROM monster_drops WHERE monster_id = @Id", owner, tx, cancellationToken: ct));

            foreach (var (sourceKey, text) in m.Labels.OrderBy(static label => label.Key, StringComparer.Ordinal))
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO monster_labels (monster_id, source_key, text) VALUES (@Id, @SourceKey, @Text)",
                    new { identity.Id, SourceKey = sourceKey, Text = text }, tx, cancellationToken: ct));
                report.LabelRows++;
            }

            if (m.AllPlayersSee || m.MaxItemsPerKill != 0 || m.DropItems.Count > 0 || m.DropsPlus.Count > 0)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO monster_drops (monster_id, max_items_per_kill, all_players_see) VALUES (@Id,@Max,@All)",
                    new { identity.Id, Max = m.MaxItemsPerKill, All = m.AllPlayersSee }, tx, cancellationToken: ct));
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
                            conn, tx, "monster_drop", identity.Key, itemCode,
                            new { Kind = d.Kind, d.Weight, d.GoldMin, d.GoldMax }, report, ct);
                        report.DropItemCodesDangling++;
                        itemCode = null;
                    }
                }
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO monster_drop_items (monster_id, kind, weight, item_code, gold_min, gold_max) " +
                    "VALUES (@Id,@Kind,@Weight,@Code,@GMin,@GMax)",
                    new { identity.Id, Kind = (int)d.Kind, d.Weight, Code = itemCode, GMin = d.GoldMin, GMax = d.GoldMax },
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
                            conn, tx, "monster_bonus_drop", identity.Key, itemCode,
                            new { p.Weight }, report, ct);
                        report.DropItemCodesDangling++;
                        itemCode = null;
                    }
                }
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO monster_drops_plus (monster_id, weight, item_code) VALUES (@Id,@Weight,@Code)",
                    new { identity.Id, p.Weight, Code = itemCode }, tx, cancellationToken: ct));
                report.MonsterDropPlusRows++;
            }
        }
    }

    private static async Task WriteNpcsAsync(
        DbConnection conn, DbTransaction tx, IReadOnlyList<ParsedNpc> npcs, IReadOnlyDictionary<string, IdentityRegistry.Entry> npcIds,
        IReadOnlyDictionary<string, string> itemCodes, ImportReport report, CancellationToken ct)
    {
        const string upsert = """
            INSERT INTO npcs (
                id, key, display_name, model, linked_file, dialog_key, sound_code, move_range, model_scale,
                portrait_offset_y, portrait_offset_z, hostile, level, size_level, appear_slot, appear_cycle)
            VALUES (
                @Id, @Key, @DisplayName, @Model, @Linked, @DialogKey, @SoundCode, @MoveRange, @ModelScale,
                @PortraitOffsetY, @PortraitOffsetZ, @Hostile, @Level, @SizeLevel, @AppearSlot, @AppearCycle)
            ON CONFLICT (id) DO UPDATE SET
                key=excluded.key, display_name=excluded.display_name, model=excluded.model, linked_file=excluded.linked_file,
                dialog_key=excluded.dialog_key, sound_code=excluded.sound_code, move_range=excluded.move_range,
                model_scale=excluded.model_scale, portrait_offset_y=excluded.portrait_offset_y,
                portrait_offset_z=excluded.portrait_offset_z, hostile=excluded.hostile, level=excluded.level,
                size_level=excluded.size_level, appear_slot=excluded.appear_slot, appear_cycle=excluded.appear_cycle;
            """;

        await DeleteStaleAsync(conn, tx, "npcs", npcIds.Values, ct);
        foreach (var n in npcs)
        {
            var identity = npcIds[n.Name];
            await conn.ExecuteAsync(new CommandDefinition(upsert, new
            {
                identity.Id, identity.Key, n.DisplayName, n.Model, Linked = n.LinkedFile, n.DialogKey, n.SoundCode, n.MoveRange,
                n.ModelScale, n.PortraitOffsetY, n.PortraitOffsetZ, n.Hostile, n.Level, n.SizeLevel, n.AppearSlot, n.AppearCycle,
            }, tx, cancellationToken: ct));
            report.NpcRows++;
            if (n.DialogKey.Length > 0)
            {
                report.NpcDialogRows++;
            }
        }

        foreach (var n in npcs)
        {
            var identity = npcIds[n.Name];
            var owner = new { identity.Id };
            await conn.ExecuteAsync(new CommandDefinition("DELETE FROM npc_labels WHERE npc_id = @Id", owner, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition("DELETE FROM npc_dialog_lines WHERE npc_id = @Id", owner, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition("DELETE FROM npc_services WHERE npc_id = @Id", owner, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition("DELETE FROM npc_shop_stock WHERE npc_id = @Id", owner, tx, cancellationToken: ct));

            foreach (var (sourceKey, text) in n.Labels.OrderBy(static label => label.Key, StringComparer.Ordinal))
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO npc_labels (npc_id, source_key, text) VALUES (@Id, @SourceKey, @Text)",
                    new { identity.Id, SourceKey = sourceKey, Text = text }, tx, cancellationToken: ct));
                report.LabelRows++;
            }

            foreach (var (sourceKey, lines) in n.DialogLines.OrderBy(static dialog => dialog.Key, StringComparer.Ordinal))
            {
                for (var ordinal = 0; ordinal < lines.Count; ordinal++)
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO npc_dialog_lines (npc_id, source_key, ordinal, text) VALUES (@Id, @SourceKey, @Ordinal, @Text)",
                        new { identity.Id, SourceKey = sourceKey, Ordinal = ordinal, Text = lines[ordinal] }, tx, cancellationToken: ct));
                    report.DialogLineRows++;
                }
            }

            for (var ordinal = 0; ordinal < n.Services.Count; ordinal++)
            {
                var service = n.Services[ordinal];
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO npc_services (npc_id, ordinal, kind, value, value2, text) VALUES (@Id,@Ordinal,@Kind,@Value,@Value2,@Text)",
                    new { identity.Id, Ordinal = ordinal, Kind = (int)service.Kind, service.Value, service.Value2, service.Text },
                    tx, cancellationToken: ct));
                report.NpcServiceRows++;
            }

            foreach (var (tab, ordinal, code) in n.Shop)
            {
                if (!itemCodes.TryGetValue(code, out var canon))
                {
                    report.ShopItemCodesDangling++;
                    // The empty-slot placeholder is kept as "none"; it holds its ordinal.
                    await RecordUnresolvedAsync(
                        conn, tx, "npc_shop", identity.Key, code == KeyMaps.DropNone ? "none" : code,
                        new { Tab = tab, Ordinal = ordinal }, report, ct);
                    continue;
                }
                report.ShopItemCodesResolved++;
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO npc_shop_stock (npc_id, tab, ordinal, item_code) VALUES (@Id,@Tab,@Ord,@Code)",
                    new { identity.Id, Tab = tab, Ord = ordinal, Code = canon }, tx, cancellationToken: ct));
                report.NpcShopRows++;
            }
        }
    }

    private async Task WriteFieldsAsync(
        DbConnection conn, DbTransaction tx, IReadOnlyList<ParsedField> fields, IReadOnlyDictionary<string, IdentityRegistry.Entry> monsterIds,
        IReadOnlyDictionary<string, IdentityRegistry.Entry> npcIds, ImportReport report, CancellationToken ct)
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
                if (!monsterIds.TryGetValue(monster, out var spawned))
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
                    "INSERT INTO field_monster_spawns (field_code, monster_id, weight) VALUES (@Code,@Mon,@W)",
                    new { Code = code, Mon = spawned.Id, W = weight }, tx, cancellationToken: ct));
                report.MonsterSpawnRows++;
            }

            foreach (var boss in f.Bosses)
            {
                if (!monsterIds.TryGetValue(boss.MasterName, out var master))
                {
                    report.BossMonsterRefsDangling++;
                    report.Warnings.Add($"field '{f.AseBase}' boss references unknown master '{boss.MasterName}'");
                    await RecordUnresolvedAsync(
                        conn, tx, "field_boss", f.AseBase, boss.MasterName,
                        new { f.AseBase, boss.SlaveName, boss.SlaveCount, boss.Hours }, report, ct);
                    continue;
                }
                report.BossMonsterRefsResolved++;
                int? escort = null;
                if (boss.SlaveName is { } slave)
                {
                    if (monsterIds.TryGetValue(slave, out var escorted))
                    {
                        escort = escorted.Id;
                    }
                    else
                    {
                        await RecordUnresolvedAsync(
                            conn, tx, "field_boss_escort", f.AseBase, slave,
                            new { f.AseBase, Boss = master.Key, boss.SlaveCount, boss.Hours }, report, ct);
                    }
                }
                var hoursCsv = string.Join(',', boss.Hours);
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO field_boss_spawns (field_code, boss_monster_id, escort_monster_id, escort_count, spawn_hours) " +
                    "VALUES (@Code,@Boss,@Escort,@Count,@Hours)",
                    new { Code = code, Boss = master.Id, Escort = escort, Count = boss.SlaveCount, Hours = hoursCsv },
                    tx, cancellationToken: ct));
                report.BossSpawnRows++;
            }

            foreach (var placement in f.FixedNpcs)
            {
                if (!npcIds.TryGetValue(placement.NpcName, out var placed))
                {
                    report.FixedNpcRefsDangling++;
                    await RecordUnresolvedAsync(
                        conn, tx, "field_fixed_npc", f.AseBase, placement.NpcName,
                        new { f.AseBase, placement.X, placement.Z, placement.Angle, placement.Y }, report, ct);
                    continue;
                }
                report.FixedNpcRefsResolved++;
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO field_fixed_npcs (field_code, npc_id, x, z, angle, y) VALUES (@Code,@Npc,@X,@Z,@A,@Y)",
                    new { Code = code, Npc = placed.Id, X = placement.X, Z = placement.Z, A = placement.Angle, Y = placement.Y }, tx, cancellationToken: ct));
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
