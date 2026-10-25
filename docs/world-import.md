# World import

`tools/PristonTale.WorldImport` (`ptworld`) turns the authored server data in `assets/GameServer` into the world
catalog tables, and the authored file formats it reads. Also covers the small `tools/PristonTale.WorldJson` and
`tools/PristonTale.DevAccounts` command-line tools.

## Read this when

- Running `import` or `verify`, or deciding which options to pass.
- A monster, item, NPC, shop, drop, spawn or NPC service is missing or wrong in game.
- Reading or changing a parser for item, monster, NPC or field files.
- Reconciling the import report, `world_entity_variants` or `world_import_unresolved_refs`.
- Importing a new kind of authored data or a new key of an existing file.
- Seeding accounts or characters for local testing.

## Code map

| Path | Responsibility |
| --- | --- |
| [Program.cs](../tools/PristonTale.WorldImport/Program.cs) | CLI: option parsing, defaults, provider setup, migration, report and `verify` output |
| [Importer.cs](../tools/PristonTale.WorldImport/Importer.cs) | Parse all sources, choose catalog entries, write every table in one transaction |
| `tools/PristonTale.WorldImport/Tokenizer.cs` | Line tokenizer of the `*key value` text format; C-style integer and float parsing |
| `tools/PristonTale.WorldImport/KeyMaps.cs` | Every recognised Korean key, value literals, resistance index maps, `ClassifyKind` |
| `tools/PristonTale.WorldImport/Cp949.cs` | CP949 decoding (registers `CodePagesEncodingProvider`) |
| `tools/PristonTale.WorldImport/ImportReport.cs` | Counters and warnings printed after `import` |
| `tools/PristonTale.WorldImport/Parsers/SourceTextReader.cs` | Reads a text file and follows its linked-file chain |
| `tools/PristonTale.WorldImport/Parsers/ItemTxtParser.cs` | Item definition files → `ParsedItem` |
| `tools/PristonTale.WorldImport/Parsers/MonsterInfParser.cs` | Monster definition files → `ParsedMonster` |
| `tools/PristonTale.WorldImport/Parsers/NpcParser.cs` | NPC definition and name files → `ParsedNpc` with typed services |
| `tools/PristonTale.WorldImport/Parsers/SpmParser.cs`, `SppReader.cs`, `SpcReader.cs` | Field spawn table, spawn points, fixed NPC placements → `ParsedField` |
| `tools/PristonTale.WorldImport/Parsers/MotionTableExtractor.cs` | Motion clip timing from model INX files |
| `tools/PristonTale.WorldImport/Parsers/Parsed*.cs` | Import DTOs; also serialized into `world_entity_variants.data_json` |
| `tools/PristonTale.WorldImport/*Export.cs` | JSON export commands without database access |
| `tools/PristonTale.WorldJson/Program.cs` | Dumps `WorldCatalog` fields, gates and warps to JSON |
| `tools/PristonTale.DevAccounts/Program.cs` | Creates accounts, load-test accounts and characters |

The importer project is not in `PristonTale.sln`; build it separately. It references the persistence providers,
Simulation, and `PristonTale.Engine3D` (for `PristonInxReader` and `PristonFileResolver`); it never initializes
graphics. `tests/PristonTale.Tests` references it.

## Commands

```bash
dotnet build tools/PristonTale.WorldImport/PristonTale.WorldImport.csproj -c Release -m:1 -warnaserror
dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- import \
  --sqlite data/pristontale.db --source assets/GameServer --assets assets
dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- verify --sqlite data/pristontale.db
```

| Command | Database | Effect |
| --- | --- | --- |
| `import` | yes | Migrate, parse, write the catalog and preservation tables, print the report and row counts. Exit 1 if the source root does not exist |
| `verify` | yes | Migrate, print row counts, orphan checks (drop items, shop items, spawn monsters), NPC services by kind, NPCs with dialog and with a display name. On a new database it creates the schema and prints zero counts |
| `export-item-names` | no | `[{Code, Name}]` of item files, first per code, to `--output` |
| `export-monster-names` | no | `[{Name, DisplayName, SourceFile}]`, first per identity; fails on a file without identity or `*Name` |
| `export-npc-placements` | no | Fixed NPC placements of every `WorldCatalog` field (field code, identity, position, angle, source file) |
| `export-npc-motions` | no | `{FormatVersion: 1, Models, Slots}` motion rows of NPC models; requires an assets root |

The usage text lists every command. Exit code 0 on success, 1 on any exception (message and stack trace on stderr)
or an unknown command.

| Option | Default | Notes |
| --- | --- | --- |
| `--sqlite <path>` | `%TEMP%/pristontale-world.db` when neither database option is given | Relative paths resolve against the working directory |
| `--postgres <conn>` | - | Npgsql connection string; wins over `--sqlite` |
| `--source <dir>` | see below | GameServer root |
| `--assets <dir>` | nearest ancestor of the executable that contains `assets/` | Root for the motion pass; the pass is skipped with a warning only when no root is found |
| `--output <path>` | - | Required by the export commands |

Options are taken by name from anywhere after the command; unrecognised arguments are ignored.

Source root lookup when `--source` is absent: `./assets/GameServer`, then `./GameServer`, then each ancestor of the
executable directory for `assets/GameServer` or `GameServer`; otherwise `./GameServer`.

## Pipeline

`Importer.RunAsync`:

1. Parse items, monsters, NPCs and fields from disk; record every parsed definition as a variant.
2. Open one connection and one transaction.
3. Delete `world_import_unresolved_refs`.
4. Write in foreign-key order: items, monsters (with drops), NPCs (services, then shop stock), fields (spawn points,
   monster spawns, bosses, fixed NPCs), motion timing, `world_source_files`, `world_entity_variants`.
5. Commit. Any exception rolls back the whole import.

Directories are matched case-insensitively (`OpenItem`, `Monster`, `NPC`, `Field`); file patterns too, because the
data mixes `.npc`/`.NPC` and `.inf`/`.INF`. Only the top level of each directory is parsed (for example
`Monster/*.inf`, not `Monster/<subdir>/`); linked files in subdirectories are reached through links. Files are processed
in ordinal path order, which makes variant selection deterministic.

## Text format

Item, monster, NPC and spawn-table files share one line format, decoded as CP949 (code page 949):

- A line is `key value value ...`. The key is the first word; words are split on space and tab.
- A value starting with `"` is read up to the closing quote and may contain spaces. Quoted text is kept verbatim,
  including leading and trailing spaces.
- There is no comment syntax. Lines whose first word is not a recognised key (`//...`, blank, prose) are ignored, so
  `//*key` disables a key.
- Numbers follow C `atoi`/`atof`: leading numeric prefix, anything invalid reads as 0, a missing value reads as 0.
- Keys are compared exactly (ordinal) unless noted: `*Name`, the `*E_*`/`*B_*` locale keys and the ASCII spawn-table
  aliases are case-insensitive.
- Linked-file key `*연결파일 <path>`: reading stops at that line and continues in the linked file. The path resolves
  segment by segment, case-insensitively, against the directory of the first file of the chain. A cycle or a missing
  file ends the chain silently.

## Parsers and file formats

### Items: `OpenItem/*.txt` (+ linked `name/*.zhoon`)

| Key (see `KeyMaps`) | Column |
| --- | --- |
| `*코드` | `code`, the 5-character natural key |
| `*Name`, then `*E_NAME`, then `*이름`, then the code | `name` (first non-empty) |
| `*내구력`, `*무게`, `*가격`, `*공격력` (4 values), `*명중력`, `*방어력`, `*흡수력`, `*블럭율`, `*이동속도`, ... | Stat ranges |
| Element keys (8) | `res_<element>_min/max` in bionic, earth, fire, ice, lighting, poison, water, wind order |
| Regen, add and restore keys | Each has two spellings (`KeyMaps.ItemManaRegen` and siblings) |
| `**특화` class names | `job_mask`: the `JobBitCode` of the last known class name (English or Korean; Assassin and Shaman included) |
| `**특화랜덤` class names | `item_template_job_random` rows in order; names of classes the game does not have (`MartialArtist`, misspellings) are skipped |
| Other `**` stat keys | `item_template_special` with a stable bonus code (`ItemTxtParser.SpecialBonusCode`, value 0 and value 1): 0 absorb, 1 defence, 2 move speed, 3 block, 4 attack speed, 5 critical, 6 range, 7 magic mastery, 8 mana per level, 9 life per level, 10 attack rating per level, 11 damage per level (second value), 12 mana regeneration, 13 life regeneration, 14 stamina regeneration, 15 life regeneration range (`**LifeRegenRange`) |
| `*최대수량`, `*보유공간`, magic mastery keys | `max_stack` (min 1), `potion_space`, `magic_mastery` |
| `*유니크` | `unique_item` (1 when the key has no value) |

`kind` comes from the code prefix (`KeyMaps.ClassifyKind`); the files have no kind key.

A file whose `*유니크` value is 2 is the quest variant of its code (the quest weapons, the rank-up rings, the Fury of
Phantom rewards): besides the catalog entry, the first such file in path order is written to `item_templates` under
the key `<code>#Q` with its own children. The runtime keeps it beside the regular catalog (`ItemCatalog.QuestVariants`)
and the quests hand it out ([quests.md](quests.md)).

### Item code mapping

An item key is a family of two letters and a letter or digit plus two digits (`WA101`, `DA304`). `ItemCodes.TryParse`
(`Shared/PristonTale.Simulation/Items/ItemCodes.cs`) derives the 32-bit item code from the key alone, so every family has
one; the importer stores it as `item_code_mask`. A key of another shape is imported with `item_code_mask = 0`, listed in
one report warning, kept in `item_templates` and skipped by the runtime catalog.

### Item footprints

`ItemFootprints.Measure` (`tools/PristonTale.WorldImport/ItemFootprints.cs`) sizes every item from its inventory sprite,
`<assets>/image/Sinimage/Items/**/it<KEY>.bmp` (`ItemIconFiles`, outside `DropItem`): one cell per whole 22 pixels in
each direction, at least one. An item without a sprite takes the most common footprint of its family (the first three
characters of its key), and one cell when the family has no sprite either; those items are listed in one report warning.
The footprint is stored in `item_templates.grid_width` and `grid_height`. Without `--assets` every item takes one cell.

`WorldDataFactory.ToItemDefinition` turns each row into the shop form plus an `ItemTemplate` with every range and the
specialization bonuses (`ToItemTemplate`); see [simulation.md](simulation.md#items).

Code lookups are case-insensitive: drop and shop references (often lower case) are rewritten to the casing stored in
`item_templates.code`.

### Monsters: `Monster/*.inf` (+ linked name files)

- Identity: `*이름`, verbatim with its spaces, because spawn tables reference it byte for byte. HUD label: `*Name`,
  trimmed; empty gives a warning.
- Model: `*모양파일` (quoted path, usually `.ini`; the runtime and motion pass use the `.inx` sibling).
- Resistances use a different key set from items and a different order; `KeyMaps.MonsterResistances` maps them to the
  same eight-element order.
- `*시야` is stored raw. `*활동시간`: `낮` day +1, `밤` night -1, anything else 0. `*품성`: `good`, `evil`, else
  natural. `*몬스터종족`: undead, mutant, demon, mechanic, else normal. `*두목` (no value) marks a boss; `*계급` sets
  the class number. `*스턴율`/`*스턴률`: flinch chance, default 100.
- Sound keys resolve through `LegacyNpcSoundCode.Resolve`; `*모델크기` is the model scale; `*화면보정 <height>
  <distance>` is the target-frame portrait correction.
- Quest keys: `*구별코드` is the classification code quest hunts count (`quest_code`); `*이벤트아이템 <code>` an item
  every kill hands out (`event_item`, rewritten to the stored code casing; an unknown code keeps its authored text and
  resolves to none at runtime); `*물약보유수` and `*물약보유률` the potions the monster carries and the chance to carry
  them (`potion_count`, `potion_percent`; arena opponents drink the count).
- Drops: `*아이템 <weight> 없음` (nothing), `*아이템 <weight> 돈 <min> <max>` (gold), or `*아이템 <weight> <code>...`,
  which becomes one row per code with `weight / count` (integer division). `*추가아이템 <weight> <code>` is an
  independent bonus drop. `*아이템모두` makes drops visible to all players; `*아이템카운터` caps items per kill.
  A `monster_drops` row is written only when one of these is present.

### NPCs: `NPC/*.npc` (+ linked `name/*.zhoon`)

- Identity `*이름` verbatim; model `*모양파일`; `*연결파일` is stored in `linked_file` and followed for more keys.
- Display name: `*E_NAME`, then `*B_NAME`, then `*Name`. If the chosen file has none, the importer takes the first
  variant with the same identity that has one; otherwise it warns and `NpcDefinition.Label` falls back to the identity.
- Dialog (at most 20 lines): all `*E_CHAT` lines if any, else `*B_CHAT`, else the Korean `*대화` lines. Runs of spaces
  and tabs collapse to one space (name files wrap text at a fixed width).
- Shop lists `*무기판매`, `*방어구판매`, `*잡화판매` fill tabs 0, 1, 2, up to 32 codes each. The placeholder `없음`
  keeps its ordinal and becomes an unresolved `npc_shop` reference; the shop exists only if a tab has a real code.
- `*이동범위` move range (default 64), sound keys, `*모델크기`, `*화면보정` as for monsters.

### NPC services

`NpcParser.BuildServices` writes one `npc_services` row per service in the order the original answers a click
(`ordinal` 0..n). Keys that set the same service keep the last value read.

| Order | Kind | Source key and value |
| --- | --- | --- |
| 1 | `QuestGiver` | `*계급 <code>` with code >= 1000 |
| 2 | `QuestScript` | `*퀘스트코드 <code> <param>` |
| 3 | `Shop` | any real stock code |
| 4 | `SkillMaster` | `*스킬수련`; value = `*직업전환` code (default 1) when present, else 0 |
| 5 | `Warehouse` | `*아이템보관` |
| 6 | `ItemMix` | `*아이템조합` value 1, `*아이템연금` value 200 |
| 7-8 | `Smelting`, `Manufacture` | `*아이템제련`, `*아이템제작` |
| 9 | `CollectMoney` | `*모금함` |
| 10-11 | `EventGift`, `GiftExpress` | `*경품추첨`, `*경품배달` |
| 12 | `WarpGate` or `QuestEvent` | `*윙퀘스트` (default 1) or `*퀘스트이벤트` (default 2) |
| 13 | `Teleporter` | `*텔레포트` route (default 1) |
| 14 | `StarPoint` | `*별포인트적립` minimum level (default 20) |
| 15 | `GiveMoney` | `*기부함` |
| 16 | `EventBooth` | `*이벤트매표소` (default 1) |
| 17-19 | `ItemAging`, `Clan`, `BlessCastle` | `*아이템에이징`, `*클랜기능`, `*블레스캐슬` (default 1) |
| 20 | `Polling` | `*설문조사` (default 1) |
| 21 | `Media` | `*동영상경로` and `*동영상제목` both present; text = path, line feed, title |
| 22 | `MixtureReset` | `*믹스처리셋` |
| 23 | `EventMonster` | `*이벤트코드`, `*이벤트정보` |
| 24+ | `Unported` | one per key in `KeyMaps.NpcUnportedServices`, text = its label |

The `svc_*` flags and `job_change_code`, `teleport_code`, `quest_code`, `quest_param` are still written for databases
read by older catalog code; see [persistence.md](persistence.md). Which services run in game is described in
[world.md](world.md) and [application.md](application.md).

### Fields: `Field/<base>.spm`, `.spp`, `.spc`

Fields come from `WorldCatalog.AllFields` (Simulation), not from the directory. The base name is the file stem of the
field's stage model path; each file is found as `<base>.<ext>` or `<base>.ase.<ext>`, case-insensitively. A field is
written only when at least one of the three files exists; `field_code` is the catalog field code.

| File | Format |
| --- | --- |
| `.spm` (text) | `*출연자`/`*ACTOR "<monster>" <weight>`; `*출연자두목`/`*BOSS_ACTOR "<boss>" "<escort>" <count> <hours...>` (up to 32 hours); `*최대동시출현수`/`*MAX_ACTOR_POS` concurrent limit; `*출현수`/`*MAX_ACTOR` monsters per wave; `*출현간격`/`*DELAY <n> [seconds]`: `spawn_interval = (1 << n) - 1`, minimum 1, optional seconds × 1000 into `per_point_cooldown_ms` |
| `.spp` (binary) | 200 records of 12 bytes, little-endian `int32 state, x, z`, no header. All-zero records are skipped; `point_index` is the record index; `state != 0` means active. Coordinates are world units |
| `.spc` (binary) | 100 records of 504 bytes, little-endian, no header. Live when `int32` at +4 is non-zero. +8: 32-byte CP949 stored name. +104: 60-byte CP949 path of the NPC definition file. +476/+480/+484: `int32 x, y, z` in 1/256 world units. +492: heading (4096 per turn) |

A fixed NPC's identity comes from the parsed NPC file named by the record's path (matched by file name). The stored
name is a fallback only, because it can be stale. The NPC files placed by any field also decide which file wins when
several share an identity.

### Motion timing from INX

`MotionTableExtractor` runs after the catalog writes, for every monster model, NPC model and player class body
(`CharacterClassRegistry` default visuals).

- Key: `MotionTimingCatalog.NormalizeKey` (extension `.inx`, forward slashes, lower case); the file is
  `<assets>/<key>`, opened through `PristonFileResolver`.
- Clips are motion entries from `PristonFormatConstants.CharacterMotionBase` on with `State != 0` and
  `EndFrame > StartFrame`; `slot` keeps the entry index. Up to four event frames and the repeat flag are stored.
- A model INX without usable clips borrows the table of its linked motion file (player bodies share one biped table);
  `motion_table_key` then names that file.
- Failures (missing or invalid INX, no link) add a warning and `MotionUnresolvedModels`; the import continues.
- `motion_models` rows are upserted; each extracted table's `motion_slots` are deleted and reinserted. A slot keeps its
  INX motion index (the clip clients play for a monster's swing) and the clip's key character (`key_code`), which
  selects the skill some monsters perform.

## Catalog entries, variants and unresolved references

| Kind | Catalog entry among files sharing an identity | Unkeyed file |
| --- | --- | --- |
| Item | File whose name equals the code (case-insensitive), else first in path order | Variant only, warning |
| Monster | First in path order | Variant only, warning |
| NPC | First file placed by a field `.spc`, else first in path order | Variant only, warning |

Every parsed file is a `world_entity_variants` row (`is_catalog_entry` marks the winner); the others also produce a
"retained outside the catalog" warning. Parse exceptions skip the file with a warning.

`world_import_unresolved_refs.kind` values:

| Kind | Owner | Effect on the catalog |
| --- | --- | --- |
| `monster_drop` | monster | Drop row kept with `item_code = NULL`, so its probability slot stays |
| `monster_bonus_drop` | monster | Bonus row kept with `item_code = NULL` |
| `npc_shop` | NPC | Stock entry not written (includes `없음` placeholders) |
| `field_monster_spawn` | field base | Spawn entry not written; warning |
| `field_boss` | field base | Boss entry not written; warning |
| `field_boss_escort` | field base | Boss written without escort |
| `field_fixed_npc` | field base | Placement not written |

`world_source_files` stores every file under the source root (including archives, executables and unparsed
subdirectories) with its SHA-256 and Base64 content. For `.txt`, `.inf`, `.inf_`, `.npc`, `.spm` and `.zhoon` it also
stores each `*key` line (line number, key, values) in `fields_json`. Preserving a key does not make it a gameplay
feature; behavior lives in Simulation and Application.

## Idempotent re-import

- Parents (`item_templates`, `monsters`, `npcs`, `fields`, `motion_models`) upsert by natural key with
  `INSERT ... ON CONFLICT ... DO UPDATE`.
- Children are deleted and reinserted per parent; the three preservation tables are cleared and rewritten.
- Running `import` twice on the same source gives the same rows (asserted by `WorldImportTests` and
  `NpcServiceTests`).
- `accounts`, `characters`, `warehouses` and `mailboxes` are never touched.
- Rows whose source disappeared are not deleted: a removed monster, NPC or item stays in the catalog with its old
  children. Import into a fresh database to drop them.

## Import report

Printed after `import`: source files and preserved key lines, variants, unresolved rows; per entity rows and files
parsed with child counts; field children; motion models, tables, clip rows and unresolved models; resolved/dangling
counts for drop items, shop items, spawn monsters, bosses and fixed NPCs; then every warning and the row counts of the
item, monster, NPC, field and preservation tables. The supplied snapshot references some files and codes it does not
contain, so dangling counts are expected; check `world_import_unresolved_refs` when reconciling data.

## Other tools

`tools/PristonTale.WorldJson` (`ptworldjson [output]`, default `world.json`): writes every `WorldCatalog` field (id,
name, code, model path stem, centre, start points, gates, warp gates) plus streaming radii and camera constants for an
external field-streaming prototype. No database.

`tools/PristonTale.DevAccounts` (`ptdevaccounts`), not in the solution:

| Command | Effect |
| --- | --- |
| `create-account <user> <password>` | Creates the account with `PasswordHasher`; existing accounts are left unchanged |
| `create-bots <prefix> <count> <password>` | Accounts `<prefix>0001`..; count 1-100000; existing ones skipped |
| `add-char <user> <name> <class> [level]` | Starter character via `CharacterRecordFactory.NewStarter` in Ricarten (field 3), first free of 6 slots; name 3-16 characters and globally unique; class by `PlayerClass` name or number |
| `list <user>` | Characters by slot |

Options: `--sqlite <path>` (default `data/pristontale.db`, resolved against the repository root), `--conn "<npgsql>"`.
Without either, the `PT_PG_CONN` environment variable selects PostgreSQL. Both options together is an error. The tool
migrates before running a command.

## How to

### Import a new key of an existing file

1. Add the token to `KeyMaps` with a comment giving its meaning in Etervale terms.
2. Read it in the parser and carry it on the `Parsed*` DTO.
3. Add the column and propagate it as in [persistence.md](persistence.md) "Add a column to a world catalog table"
   (migration, record, repository, upsert column and `SET` lists, `WorldDataFactory`, Simulation definition).
4. Extend the CP949 fixture of `tests/PristonTale.Tests/WorldImportTests.cs` (or `NpcServiceTests`) and assert the
   value after two import runs.

### Import a new kind of authored data

1. Schema: a structural migration with a parent table keyed by the authored identity, child tables with identity keys,
   cascading foreign keys and parent indexes ([persistence.md](persistence.md)).
2. Parser: `Parsers/<Kind>Parser.cs` returning `Parsed<Kind>`. Use `SourceTextReader.ReadChain` for linked text files,
   `Tokenizer` for lines, `Cp949` for bytes, `BinaryPrimitives` little-endian for binary records; tokens in
   `KeyMaps`. Keep identities verbatim if other files reference them.
3. Importer: a `Parse<Kind>` method that enumerates with `EnumerateFiles`, chooses the catalog entry, and calls
   `CreateVariant` (kind at most 16 characters); a `Write<Kind>Async` in the transaction, after the tables it
   references, that upserts parents, deletes and reinserts children, and calls `RecordUnresolvedAsync` (kind at most
   32 characters) for every reference it cannot resolve.
4. Report: counters in `ImportReport`, a line in `PrintReport`, the tables in `PrintRowCounts`, and an integrity
   query in `PrintCrossRefCheck` when the kind has references.
5. Preservation: add text extensions with `*key` lines to `Importer.ReadSourceFields`.
6. Runtime: record and read method in `IWorldDataRepository`/`DapperWorldDataRepository`, conversion in
   `WorldDataFactory`, loading in `WorldDataAppService.BuildAsync` and `WorldData`.
7. Tests: a disposable-database test in `tests/PristonTale.Tests` that writes CP949 fixtures, imports twice and
   checks rows, variants and unresolved references; register it in the runner.
8. Docs: this file, [persistence.md](persistence.md), and the README import section if commands change.

## Pitfalls

- Never trim identity names: spawn tables and placements reference monster and NPC identities with their spaces.
- A key after `*연결파일` in the same file is never read; put it before the link or in the linked file.
- Missing linked files produce no warning and no unresolved row.
- Monster drop weights for multi-code lines are split with integer division, as the original does; the remainder
  is lost.
- `world_entity_variants.data_json` serializes the DTOs with `IncludeFields`, so value-tuple lists (item specials,
  NPC shop stock) appear as `Item1`, `Item2`... objects. `world_source_files` holds the complete source.
- `--assets` is not required inside the repository: the default finds the repository `assets/` from the executable
  path. Pass it explicitly when running a published copy.
- The default database without `--sqlite`/`--postgres` is a temporary file, not `data/pristontale.db`.
- On a legacy-upgraded database, NPC display names and services stay empty until `import` runs again.

## Related

- [persistence.md](persistence.md): schema, migrations, providers, repository reads.
- [world.md](world.md): spawning, NPCs, loot and motion at runtime.
- [simulation.md](simulation.md): item and monster definitions.
- [rendering.md](rendering.md): INX format and `PristonFileResolver`.
- [tools.md](tools.md): other tools.
- [testing.md](testing.md): running the regression suite.
