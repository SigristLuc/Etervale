# Persistence

Accounts, characters, warehouses and the imported world catalog: repository contracts, Dapper implementations,
FluentMigrator migrations, the schema, and the SQLite and PostgreSQL providers. Projects: `Persistence/*`.

## Read this when

- Adding or changing a column, table, index or migration.
- Changing what a character, inventory, skill set or warehouse saves, or how saved JSON is validated.
- Debugging a Dapper mapping error (`A parameterless default constructor or one matching signature ...`).
- Switching the server or tools between SQLite and PostgreSQL, or upgrading an old database.
- Making the runtime read a new world-catalog field produced by the importer.
- Investigating a refused warehouse write or a character that fails to load.

## Code map

| Path | Responsibility |
| --- | --- |
| `Persistence/PristonTale.Persistence.Abstractions/` | Provider-neutral contracts and records; references only Simulation |
| `.../IDbConnectionFactory.cs`, `IDatabaseMigrator.cs` | Open a provider connection; apply pending migrations |
| `.../Accounts/` | `AccountRecord` (with `GmLevel`), `IAccountRepository` |
| `.../GameMasters/IGameMasterRepository.cs` | Set a game-master level, append and read the audit trail, move a character between accounts |
| `.../Characters/CharacterRecord.cs` | One `characters` row; positional record, JSON columns as `JsonText` |
| `.../Characters/CharacterJson.cs` | `JsonText` wrapper, shared JSON options, `DerivedStatsDto` |
| `.../Characters/CharacterStateCodec.cs` | Owned-state JSON (`SavedItems`, `SavedSkills`): capture, strict restore |
| `.../Characters/CharacterRecordFactory.cs` | New starter rows; capture a live `PlayerCharacter` into a save snapshot |
| `.../Warehouses/` | `WarehouseRecord`, `IWarehouseRepository`, `WarehouseStateCodec` |
| `.../WorldData/` | Read-side catalog records and [WorldDataFactory](../Persistence/PristonTale.Persistence.Abstractions/WorldData/WorldDataFactory.cs) |
| `.../Security/PasswordHasher.cs` | PBKDF2-SHA256 hashes: `pbkdf2-sha256$<iterations>$<salt b64>$<key b64>` |
| `Persistence/PristonTale.Persistence/Repositories/` | `Dapper*Repository` implementations shared by both providers |
| `Persistence/PristonTale.Persistence/Migrations/` | `M013Baseline`, `M014PortraitData`, `M015NpcServices`, `M016QuestData`, `M019GameMasters`, `FluentMigratorDatabaseMigrator` |
| `Persistence/PristonTale.Persistence.Sqlite/` | `AddPristonSqlite`, connection factory (WAL), Dapper type handlers, path resolution |
| `Persistence/PristonTale.Persistence.Postgres/` | `AddPristonPostgres`, `NpgsqlDataSource` factory, `jsonb` handler for `JsonText` |
| `docker-compose.yml` | Local PostgreSQL 17 for development |

Consumers: `Server/PristonTale.Server/Hosting/HostingExtensions.cs` (provider selection),
`DatabaseMigrationService` (migrates at host start), `Shared/PristonTale.Application` services
(`AuthAppService`, `CharacterAppService`, `SessionAppService`, `NpcAppService`, `WorldDataAppService`),
`tools/PristonTale.WorldImport` (owns all catalog write SQL) and `tools/PristonTale.DevAccounts`.

## Repository contracts

| Interface | Implementation | Operations |
| --- | --- | --- |
| `IAccountRepository` | `DapperAccountRepository` | `FindByUserIdAsync`, `CreateAsync` (INSERT ... RETURNING), `UpdateLastLoginAsync` |
| `ICharacterRepository` | `DapperCharacterRepository` | List by account (ordered by slot), find by name for account, global `NameExistsAsync`, `UsedSlotsAsync`, create (RETURNING), delete by name, `UpdateAsync` |
| `IWarehouseRepository` | `DapperWarehouseRepository` | `FindAsync`, `SaveWithCharacterAsync` (character + warehouse in one transaction) |
| `IWorldDataRepository` | `DapperWorldDataRepository` | Read-only bulk loads: items, monsters, NPCs, field spawns, motion models, motion slots |

- All repositories are DI singletons; every call opens its own connection through `IDbConnectionFactory` and
  disposes it. There is no unit of work across calls.
- `IWorldDataRepository` is read-only by design. Catalog writes live in the importer (`Importer`), see
  [world-import.md](world-import.md).
- `UpdateAsync` writes every mutable column; identity columns (`id`, `account_id`, `name`, `slot`, `job_code`,
  `created_at`) are never updated. It returns false when no row matched.
- Child collections are loaded with one query per child table and joined in memory with `ToLookup` on the parent
  natural key (`item_code`, `monster_name`, `npc_name`, `field_code`).

### Runtime catalog build

`WorldDataAppService.BuildAsync` reads all six catalog sets and converts them with `WorldDataFactory`:

| Factory method | Contract |
| --- | --- |
| `ToItemDefinitions` | Skips rows with `item_code_mask = 0`. Runtime item code = `item_code_mask`; `CatalogKey` = `code`. Base stats are midpoints of the min/max columns |
| `ToMonsterDefinitions` | Runtime code = 1-based index in `ORDER BY name`. The stored model path keeps its authored extension; the definition uses the `.inx` sibling (also for NPCs). Drops resolve item codes through the runtime catalog by `CatalogKey`; unresolved main drops stay as empty slots that keep their probability |
| `ToNpcDefinitions` | Runtime code = 1-based index in `ORDER BY name`. Services from `npc_services` ordered by `ordinal`; unknown kinds skipped; falls back to `svc_*` flags only when the NPC has no service rows; `Shop` removed when no stock code maps to a runtime item |
| `ToFieldSpawnData` | Resolves monster and NPC names to runtime codes; entries whose name is unknown are dropped; `spawn_hours` CSV parsed to hours |
| `ToMotionTimings` | Groups `motion_slots` by table in row order, then maps each `motion_models.model_key` to its table |

## Dapper rules

- Typed queries only: `QueryAsync<T>`, `QuerySingleAsync<T>`, `ExecuteScalarAsync<T>`, value tuples for ad-hoc
  projections. Never the untyped `Query(...)`, which yields `dynamic` (AGENTS.md forbids it).
- Wrap SQL in `new CommandDefinition(sql, parameters, transaction, cancellationToken: ct)` so cancellation and the
  transaction flow through.
- Parameters are anonymous objects or the record itself (`@PascalCase` names from record properties).
- `DefaultTypeMap.MatchNamesWithUnderscores = true` is set by both providers: `snake_case` columns map to
  `PascalCase` members.
- Records are positional. Dapper picks a constructor whose parameter count equals the column count and whose
  parameter names match the columns in order. The selected column list must therefore follow the constructor order.
  `item_templates` and `monsters` are read with `SELECT *`, so their physical column order is part of the contract.
- JSON columns bind as `JsonText`, never as `string`: each provider registers a `JsonText` handler (`TEXT` on SQLite,
  `NpgsqlDbType.Jsonb` on PostgreSQL). A plain string parameter against a `jsonb` column fails on PostgreSQL.
- SQL is shared by both providers: `INSERT ... RETURNING`, `INSERT ... ON CONFLICT ... DO UPDATE`, `excluded.*`,
  `SELECT EXISTS (...)`. Keep new SQL in that common subset.

## Transactions and concurrency

| Write | Scope |
| --- | --- |
| Account, character create, update, delete | Single statement, autocommit |
| `SaveWithCharacterAsync` | One transaction: character `UPDATE` (shared `DapperCharacterRepository.UpdateSql`) then warehouse write; rollback and `false` if either touches no row |
| World import | One transaction for the whole import run (see [world-import.md](world-import.md)) |
| Migrations | FluentMigrator runner, synchronous `MigrateUp()` inside `FluentMigratorDatabaseMigrator.MigrateAsync` |

Warehouse optimistic concurrency: `warehouses.revision` must equal `expectedRevision`. A missing row counts as
revision 0, so only `expectedRevision == 0` may insert (`ON CONFLICT (account_id) DO UPDATE ... WHERE
warehouses.revision = 0`). The caller (`NpcAppService`) supplies the new revision in the record. The character
and its account warehouse are always written together, so a gold or item move between them cannot half-apply.

## Owned-state JSON

`CharacterStateCodec`, `SavedPremium`, `QuestStateCodec` and `WarehouseStateCodec` own the JSON stored in
`characters.inventory`, `equipment`, `skills`, `premium`, `quests` and `warehouses.items`.

| Column | Shape | Limits on restore |
| --- | --- | --- |
| `inventory` | `SavedItems` v1: items with `Anchor` (grid cell), `Slot = null`, plus `Revision`, inventory `Operations`, `ServiceOperations` | 72 items |
| `equipment` | `SavedItems` v1: items with `Slot` set and `Anchor = -1` | 72 items |
| `skills` | `SavedSkills` v1: points, elite points, `(Code, Rank, Uses)`, progression `Revision` and `Operations` | 128 skills, points >= 0 |
| `warehouses.items` | `SavedItems` v1 with anchors in the warehouse grid | `WarehouseStorage.MaxItems` |
| `derived_stats` | `DerivedStatsDto`, written on save; nothing reads it | - |
| `premium` | `SavedPremium` v1 (`PremiumStateCodec.cs`): premium timers `(Kind, Value, DurationSeconds, ExpiresAtSeconds)` with Unix-second expiry, item-use `Operations`, `GravityScrolls`, head mode (1-2 big head, 3 soccer ball) and expiry, `SavedAtSeconds` (the last tick's clock); `{}` is empty | 17 timers; expired timers and heads are dropped on restore; boosters and skill delay move their expiry by the time since `SavedAtSeconds` |
| `quests` | `SavedQuests` v1: the running quest (`SavedQuest`), level and beginner logs, finished codes, `FinishedCount`, the redistribution offer, `Revision` and quest `Operations` (see [quests.md](quests.md)); `{}` reads as an idle journal | 32 finished codes, 64 operations, defined quest codes and timer kinds, non-negative counts |

- Restore is strict: unknown JSON members are rejected, constructor parameters without defaults are required, max
  depth 16, max 65536 characters, `Version` must be 1, instance ids must be unique and non-empty, the item code must
  exist in the runtime `ItemCatalog`, counts must fit `MaxStack`, `UpgradeLevel` must not be negative. Any violation
  throws `InvalidDataException`.
- Each `SavedItem` carries the copy's `Roll` (`ItemRoll`: rolled stats, requirements, price, specialization and its
  bonuses). An item saved without one (older rows) restores in its shop form with its saved `JobSpecMask`;
  `RolledBonus` is read and ignored. Saved equipment only needs to fit its slot (`PlayerCharacter.CanHold`): a worn item
  whose requirements are no longer met is restored worn.
- `[]` (inventory, skills, warehouse) and `{}` (equipment) are the empty legacy values and restore as empty state.
- An inventory item whose saved anchor no longer fits its footprint moves to the first free cell.
- `SavedItem.Unique` is 2 for the quest variant of a code (quest weapons, the rank-up ring, Fury rewards); such an item
  restores as the code's quest variant, or as the regular definition when the catalog has no variant.
- Enums serialize as strings (`JsonStringEnumConverter`).

## Migrations

FluentMigrator scans `PristonTale.Persistence` for `[Migration(n)]` classes. Version history is in the
`VersionInfo` table. The server, the importer and DevAccounts all migrate before doing anything else.

| Version | Class | Effect |
| --- | --- | --- |
| 13 | `M013Baseline` | Empty database: creates every table below except `npc_services`, then on PostgreSQL converts the JSON columns to `jsonb`. Database with an `accounts` table: requires `VersionInfo` to contain version 12, then adds `item_templates.max_stack/potion_space/magic_mastery`, `monsters.sound_code/model_scale`, `npcs.model_scale` and creates the three `world_*` source tables |
| 14 | `M014PortraitData` | `monsters.portrait_offset_y/z`; `npcs.display_name` (128), `npcs.portrait_offset_y/z`. Columns append in record constructor order |
| 15 | `M015NpcServices` | `npc_services` table and index; `warehouses.revision`; `npcs.display_name` only if missing |
| 16 | `M016QuestData` | `monsters.quest_code`, `event_item` (item code key, empty when none), `potion_count`, `potion_percent` |
| 19 | `M019GameMasters` | `accounts.gm_level` (default 0); `gm_audit` table and its `account_id` index |

Legacy upgrade path: databases from the earlier migration set at version 12 upgrade in place. Databases at
versions 1-11 make `M013Baseline` throw; bring them to 12 with the previous release or back up and recreate them.
After upgrading, run `import` again so existing rows receive the new columns (display names, services, portrait data).

The PostgreSQL `jsonb` columns are exactly `M013Baseline.JsonColumns`: `characters.inventory`, `equipment`, `skills`,
`quests`, `derived_stats`, `premium`, `warehouses.items`, `mailboxes.items`, `npcs.dialog`. The `*_json` columns of the
`world_*` tables are plain text on both providers.

## Schema

`PK` primary key, `FK` foreign key (`cascade` unless noted). Integer columns are `int32` unless stated.

### Accounts and security

| Table | Columns and constraints |
| --- | --- |
| `accounts` | `id` int64 identity PK; `user_id` unique; `password_hash` (`PasswordHasher` format); `is_blocked`, `is_tester` bool; `created_at` UTC default; `last_login_at` nullable; `gm_level` 0 to 4 (default 0, player) |
| `gm_audit` | `id` int64 identity PK; `account_id` int64 (indexed, no FK so the trail outlives the account); `user_id`, `character_name`, `gm_level`, `command`, `required_level`, `accepted` bool, `result`; `created_at` UTC default |

### Characters

| Table | Columns and constraints |
| --- | --- |
| `characters` | `id` int64 identity PK; `account_id` FK `accounts`; `name` globally unique; `slot` int16, unique with `account_id`; `job_code` (`PlayerClass`); `level`, `exp` int64, `money` int64, `brood`; attributes `strength`, `spirit`, `talent`, `agility`, `health`, `stat_points`; vitals `life`, `mana`, `stamina`; `field_id`, `pos_x`, `pos_z` float; `model_name`, `model_name2` (head model for the chosen face); `face_index`, `tier` int16, `hair_index`, `size_level`; JSON `inventory`, `equipment`, `skills`, `quests`, `derived_stats`, `premium`; `created_at`, `last_played_at` nullable. Index `characters_account_id_idx` |

### Inventory and warehouse

| Table | Columns and constraints |
| --- | --- |
| `warehouses` | `account_id` PK and FK `accounts`; `money` int64; `revision` int64 (M015); `items` JSON; `buy_area_count` (no code reads or writes it) |
| `mailboxes` | `account_id` PK and FK `accounts`; `items` JSON. Schema only; no code reads or writes it |

Character inventory and equipment are JSON columns of `characters`, not tables.

### World catalog

Written only by the importer. Natural keys are the authored identities, kept verbatim.

| Table | Columns and constraints |
| --- | --- |
| `item_templates` | `code` (16) PK, authored 5-character code; `item_code_mask` int64 (0 = category not mapped, excluded at runtime); `name`; `kind` (`ItemKind`); durability, weight, price; damage min/max low/high; attack rating, critical, defence, block, absorb, speed; mana/life/stamina regen (float) and add; requirements; potion restore min/max; `shooting_range`, `attack_speed`; `job_mask`, `unique_item` int64; `res_<element>_min/max` for bionic, earth, fire, ice, lighting, poison, water, wind; `max_stack`, `potion_space`, `magic_mastery`. A row keyed `<code>#Q` holds the code's quest variant (unique marker 2); it loads into `ItemCatalog.QuestVariants`, not the regular catalog |
| `item_template_special` | `id` PK; `item_code` FK `item_templates`; `bonus_code`; `value0`, `value1` float |
| `item_template_job_random` | `id` PK; `item_code` FK; `ordinal`; `job_bit_code` int64 |
| `monsters` | `name` PK (identity, may contain leading or trailing spaces); `model`; combat stats; `sight` (raw authored reach); skill damage, distance, range, rating, curse; `move_speed`, `move_range`, `iq`, `group_min/max`, `exp`, `active_hour` (+1 day, -1 night, 0 any); `nature`, `brood`, `class` int16; `res_<element>`; `stun_pct` (default 100); `display_name` (128); `sound_code`; `model_scale`; `portrait_offset_y/z`; `quest_code`, `event_item`, `potion_count`, `potion_percent` (M016) |
| `monster_drops` | `monster_name` PK and FK; `max_items_per_kill`; `all_players_see` |
| `monster_drop_items` | `id` PK; `monster_name` FK; `kind` (`DropKind`); `weight`; `item_code` nullable FK `item_templates` (set null); `gold_min/max` |
| `monster_drops_plus` | `id` PK; `monster_name` FK; `weight`; `item_code` nullable FK (set null) |
| `npcs` | `name` PK (identity); `model`; `linked_file`; 16 `svc_*` bools; `job_change_code`, `teleport_code`, `quest_code`, `quest_param`; `dialog` JSON string array; `sound_code`; `move_range` (default 64); `model_scale`; `display_name`; `portrait_offset_y/z` |
| `npc_shop_stock` | `id` PK; `npc_name` FK; `tab` (0 weapons, 1 armour, 2 goods); `ordinal`; `item_code` FK `item_templates` |
| `npc_services` | `id` PK; `npc_name` FK; `ordinal` (click order); `kind` (`NpcServiceKind`); `value`, `value2`; `text`. Index `npc_services_npc_idx` |
| `fields` | `field_code` PK (`WorldCatalog` field code); `ase_base`; `max_concurrent_monsters` (10); `max_per_spawn_point` (3); `spawn_interval` (tick mask); `per_point_cooldown_ms` |
| `field_spawn_points` | `id` PK; `field_code` FK; `point_index`; `state` (0 inactive); `x`, `z` world units |
| `field_monster_spawns` | `id` PK; `field_code` FK; `monster_name` FK `monsters`; `weight` |
| `field_boss_spawns` | `id` PK; `field_code` FK; `boss_monster_name` FK `monsters`; `escort_monster_name` nullable, no FK; `escort_count`; `spawn_hours` CSV |
| `field_fixed_npcs` | `id` PK; `field_code` FK; `npc_name` FK `npcs`; `x`, `y`, `z` in 1/256 world units; `angle` (4096 per turn) |
| `motion_models` | `model_key` (256) PK, normalized model path; `motion_table_key` |
| `motion_slots` | PK (`motion_table_key`, `slot`); `state`; `start_frame`, `end_frame`; `event_frame0..3` int64 holding unsigned 32-bit values; `repeat` |

Child tables with an identity key have an index on their parent key (`<table>_<parent>_idx`). `motion_models` and
`motion_slots` have no foreign key between them.

### World source preservation

These tables keep what the catalog tables cannot express. They are rewritten on every import.

| Table | Columns | Content |
| --- | --- | --- |
| `world_source_files` | `source_path` (1024) PK, `kind` (32), `sha256` (64), `content_base64`, `fields_json`, `field_count` | Every file under the source root, byte for byte. `kind` is the lower-cased first path segment (`openitem`, `monster`, `npc`, `field`). `fields_json` lists the `*key` lines of text files with line number and values |
| `world_entity_variants` | `source_path` PK, `kind` (16: `item`, `monster`, `npc`), `identity_key` (256), `is_catalog_entry`, `data_json` | Every parsed definition, including duplicates and unkeyed files; `is_catalog_entry` marks the one written to the catalog |
| `world_import_unresolved_refs` | `id` PK, `kind` (32), `owner_key`, `raw_reference`, `details_json` | References that did not resolve; kinds listed in [world-import.md](world-import.md) |

## Providers

| Aspect | SQLite (`AddPristonSqlite`) | PostgreSQL (`AddPristonPostgres`) |
| --- | --- | --- |
| Connection | New `SqliteConnection` per call, `Foreign Keys=True` | Pooled `NpgsqlDataSource` singleton |
| Journal | First open per factory runs `PRAGMA journal_mode=WAL` (persisted in the file); every open runs `PRAGMA synchronous=NORMAL` | Server default |
| JSON columns | `TEXT` | `jsonb` (converted in `M013Baseline`) |
| Type handlers | `SqliteTypeMapping`: bool as 0/1, int16/int32 sent as int64, float sent as double, `JsonText` as string, `DateTime` read as UTC | `JsonTextNpgsqlHandler` only |
| Registration | `SqliteTypeMapping.EnsureRegistered()` runs from both the DI extension and `SqliteConnectionFactory` | In `AddPristonPostgres` |
| Path | Server and DevAccounts resolve relative paths against the directory holding `PristonTale.sln` (`SqliteDatabasePath.Resolve`); the parent directory is created | Connection string |

SQLite writes `-wal` and `-shm` files next to the database while in use; copy all three, or stop every process
first, when copying a database.

Server configuration (`Server/PristonTale.Server/appsettings.json`): `Database:Provider` (`Sqlite` default or
`Postgres`), `Database:SqlitePath` (default `data/pristontale.db`), `ConnectionStrings:Postgres`. Any other provider
value stops startup.

`docker-compose.yml` runs `postgres:17` as container `pristontale-postgres`, database, user and password
`pristontale`, host port 5433 mapped to 5432, named volume `pristontale-pgdata`, health check `pg_isready`. Start with
`docker compose up -d --wait`; `docker compose down -v` deletes the volume. The default connection string in
`appsettings.json` matches it.

## How to

### Add a column to a world catalog table

1. New migration `Persistence/PristonTale.Persistence/Migrations/M0NN<Name>.cs` with the next `[Migration(NN)]`:
   `Alter.Table(...).AddColumn(...).NotNullable().WithDefaultValue(...)` and a matching `Down`. Never edit an
   applied migration; `M013Baseline` has already run on existing databases.
2. Record in `Persistence/PristonTale.Persistence.Abstractions/WorldData/`: append a constructor parameter with a
   default, at the end, so constructor order equals column order on new and upgraded databases.
3. Repository `DapperWorldDataRepository`: add the column to the explicit select lists (`npcs`, `fields`, child
   tables). `item_templates` and `monsters` use `SELECT *` and follow the column order.
4. Importer `tools/PristonTale.WorldImport`: parsed DTO in `Parsers/Parsed*.cs`, the parser, and both the column
   list and the `ON CONFLICT ... DO UPDATE SET` list of the upsert in `Importer.Write*Async`.
5. Runtime catalog: map it in `WorldDataFactory` into the Simulation definition (`ItemDefinition`,
   `MonsterDefinition`, `NpcDefinition`, `FieldSpawnData`).
6. Both providers: new CLR types need a SQLite handler in `SqliteTypeMapping` if Microsoft.Data.Sqlite cannot bind
   them; a JSON column needs `JsonText` and an `IfDatabase("PostgreSQL")` conversion to `jsonb` in the migration.
7. Tests in `tests/PristonTale.Tests`: `WorldImportTests`, `NpcServiceTests`, `MonsterNameTests` and the schema
   checks of `NpcPlacementMigrationTests` use disposable SQLite databases; extend the relevant one. Check PostgreSQL
   with `docker compose` and `ptworld import --postgres ...` against a disposable database.
8. Update this document's schema table and [world-import.md](world-import.md).

### Add a world catalog table

Steps 1, 4-8 above, plus: create the table with a natural or identity key, foreign keys with `OnDelete(Cascade)` to
the parent, and an index on the parent key; add a record and an `IWorldDataRepository` method with
`DapperWorldDataRepository` and every test double; load it in `WorldDataAppService.BuildAsync`; write it in the
importer's transaction in foreign-key order; add it to `PrintRowCounts` in `tools/PristonTale.WorldImport/Program.cs`.

### Add a character column

1. Migration as above.
2. Append the parameter to `CharacterRecord` (no default needed if every construction site passes it).
3. `DapperCharacterRepository`: `Columns`, the `INSERT` column and value lists, and `UpdateSql` (unless immutable).
4. `CharacterRecordFactory.NewStarter` and `Capture`; check `CharacterAppService`, `SessionAppService` and
   `tools/PristonTale.DevAccounts`, which build records through the factory.
5. If it is JSON, use `JsonText`, add the column to the PostgreSQL `jsonb` conversion of the new migration.

### Add a field to saved inventory, skills or warehouse JSON

Add a constructor parameter with a default to `SavedItem`, `SavedItems`, `SavedSkill` or `SavedSkills`, so rows
written before the change still deserialize under `RespectRequiredConstructorParameters`. Removing a member breaks
old rows (`UnmappedMemberHandling.Disallow`); bump `Version` and add a conversion instead.

## Pitfalls

- Column order is a contract for `SELECT *` reads: a column added out of constructor order breaks `item_templates`
  or `monsters` mapping at server start.
- Runtime monster and NPC codes are recomputed from `ORDER BY name` at every catalog build. Adding or renaming a
  monster shifts the codes of later names; never store them.
- `GetAllMotionSlotsAsync` must return rows ordered by `(motion_table_key, slot)`; `ToMotionTimings` groups
  consecutive rows and the first slot per state is the clip the client plays.
- Dapper configuration is process-global (`MatchNamesWithUnderscores`, type handlers). Use one provider per
  process; the SQLite int and float handlers would also apply to PostgreSQL parameters.
- A saved item whose code is missing from the runtime catalog makes the character or warehouse fail to load. Item
  codes removed from the source, or whose category maps to `item_code_mask = 0`, affect existing saves.
- `M013Baseline.Down` drops every table, including on a database that came through the legacy upgrade branch.
- `M015NpcServices` checks `npcs.display_name` before adding it because `M014PortraitData` already adds it.
- `svc_*` columns and their codes are still written by the importer; the runtime reads them only for an NPC with no
  `npc_services` rows. Keep them in sync with `NpcParser.BuildServices` until they are removed.
- The importer resolves a relative `--sqlite` path against the working directory, unlike the server and DevAccounts.

## Related

- [world-import.md](world-import.md): how the catalog and preservation tables are filled.
- [application.md](application.md): when characters and warehouses are loaded and saved.
- [server.md](server.md): host configuration and startup order.
- [simulation.md](simulation.md), [world.md](world.md): the definitions the catalog becomes.
- [testing.md](testing.md): the regression runner.
