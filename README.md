# Etervale

Etervale is a C# and MonoGame reimplementation of Priston Tale. The online server and the local offline mode use the same simulation
and authoritative game logic. PostgreSQL stores online data; SQLite stores offline data.

This README covers a working local setup, including the world database migration and data import. Commands below run
from the repository root and use the Release configuration.

## Requirements

- .NET 10 SDK.
- The game asset archive (download link **to be added by the maintainer**). Extract it at the repository root so the
  archive's `assets/` directory appears beside this README.
- The original server configuration files for a complete world import. Place the `GameServer/` directory at the
  repository root, or pass its actual location with `--source`.
- A working graphics environment for the client. On Linux, shader compilation also requires Wine and a MonoGame MGFXC
  Wine prefix; set `MGFXC_WINE_PATH` to that prefix when building.
- Docker with Compose only if you want the online PostgreSQL server. Offline play does not require Docker or PostgreSQL.

The game assets and `GameServer/` are excluded from Git. They are separate inputs: `assets/` contains models,
textures, audio, HUD resources, and INX animation data; `GameServer/` contains the authored item, monster, NPC, and field
configuration read by the importer. The asset archive must include `assets/Hud/Fonts/PristonUiSans-Regular.otf` for the
MonoGame content build.

Expected layout:

```text
PristonTale/
├── assets/
│   ├── char/
│   ├── Field/
│   ├── Effect/
│   ├── image/
│   ├── sky/
│   ├── wav/
│   └── Hud/
└── GameServer/
    ├── OpenItem/
    ├── Monster/
    ├── NPC/
    └── Field/
```

The paths above are defaults, not hardcoded requirements for the importer. The importer looks for `GameServer/` in the
working directory or an ancestor of its executable. The client finds `assets/` by walking upward from its executable
directory. The importer also accepts explicit `--source` and `--assets` paths.

## Build

On Linux, replace `/path/to/wine-prefix` with your own MGFXC Wine prefix:

```bash
dotnet restore PristonTale.sln -warnaserror
MGFXC_WINE_PATH=/path/to/wine-prefix dotnet build PristonTale.sln -c Release -m:1 -warnaserror
dotnet build tools/PristonTale.WorldImport/PristonTale.WorldImport.csproj -c Release -m:1 -warnaserror
```

On Windows, build the solution and importer in Release without the `MGFXC_WINE_PATH=...` prefix. The standalone importer
build is explicit because its Release output must be available for the `--no-build` commands below.

## Offline: migrate, import, and play

The offline client opens `PristonTale/offline.db` under the operating system's local application data directory. On
Linux this is normally `$HOME/.local/share/PristonTale/offline.db`; on Windows it is normally
`%LOCALAPPDATA%\PristonTale\offline.db`. Use the exact file the client opens so that the imported world appears in the
game.

For a Linux shell:

```bash
mkdir -p "$HOME/.local/share/PristonTale"
OFFLINE_DB="$HOME/.local/share/PristonTale/offline.db"

dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- import \
  --sqlite "$OFFLINE_DB" \
  --source GameServer \
  --assets assets

dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- verify \
  --sqlite "$OFFLINE_DB"

dotnet run --project Client/PristonTale -c Release --no-build
```

For Windows PowerShell, use the same importer with the client's local application data path:

```powershell
$offlineDb = Join-Path $env:LOCALAPPDATA 'PristonTale\offline.db'
New-Item -ItemType Directory -Force (Split-Path $offlineDb) | Out-Null

dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- import `
  --sqlite $offlineDb `
  --source GameServer `
  --assets assets

dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- verify --sqlite $offlineDb
dotnet run --project Client/PristonTale -c Release --no-build
```

Choose **Play Offline** on the login screen. An empty account name uses the local profile `Player`. The offline backend
opens the same SQLite file and applies pending migrations when it starts. The importer must be run separately to
populate world data.

The `import` command applies the schema migration **before** reading the source files; there is no separate migration
command. `verify` also applies pending migrations, then prints table counts and persisted reference checks. Repeating
`import` with the same source updates the catalog and replaces its variable child rows without duplicating them. It does
not delete player accounts or characters.

To apply the migration without importing world data, run `verify` against an empty database with `--sqlite` or `--postgres`.
It creates the schema and then reports zero catalog rows. Run `import` afterward to populate those rows.

### Start with a fresh offline database

If your existing `offline.db` has an intermediate schema or you want a clean test save, close the game, back up the
file, and remove it before running `import`:

```bash
OFFLINE_DB="$HOME/.local/share/PristonTale/offline.db"
cp "$OFFLINE_DB" "$OFFLINE_DB.backup"
rm "$OFFLINE_DB"
```

Then run the offline import and verification commands above. This resets local accounts and characters in the new
database. Skip this step for a new installation where `offline.db` does not yet exist.

## Online: migrate, import, and connect

`docker-compose.yml` defines the local development PostgreSQL server on host port **5433**. Its database, user, and
password match the default connection string in `Server/PristonTale.Server/appsettings.json`. Change these development
credentials before using the stack outside a local machine.

```bash
docker compose up -d --wait
dotnet build tools/PristonTale.DevAccounts/PristonTale.DevAccounts.csproj -c Release -m:1 -warnaserror

dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- import \
  --postgres "Host=localhost;Port=5433;Database=pristontale;Username=pristontale;Password=pristontale" \
  --source GameServer \
  --assets assets

dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- verify \
  --postgres "Host=localhost;Port=5433;Database=pristontale;Username=pristontale;Password=pristontale"

dotnet run --project tools/PristonTale.DevAccounts -c Release --no-build -- create-account demo change-me
dotnet run --project Server/PristonTale.Server -c Release --no-build
```

Run the client in another terminal:

```bash
dotnet run --project Client/PristonTale -c Release --no-build
```

Log in with the development account you created. The client currently connects to `http://localhost:10012`; the server
listens on that port using HTTP/2. The server also applies pending migrations at startup, but it does **not** run the
world importer. Import before starting the server so it loads populated catalogs.

To use a different PostgreSQL server, pass the same connection string to the importer's `--postgres` option, set
`ConnectionStrings__Postgres` for the server, and pass `--conn` to `PristonTale.DevAccounts`. `docker compose down`
stops the local database while keeping its named volume; `docker compose down -v` also deletes that volume and its data.

## What the migration and importer do

The current structural migration is `M013Baseline` in `Persistence/PristonTale.Persistence/Migrations/`. It creates the
full schema on an empty database and can add the new columns and source tables to an existing database already at legacy
migration version 12. Databases stopped at intermediate versions 1–11 must first reach version 12 with the previous
migration set or be backed up and recreated.

`PristonTale.WorldImport` reads the CP949 `GameServer` files and builds the item, monster, NPC, and field catalogs. It
follows linked text files, maps original item and sound codes, imports supported gameplay fields, and reads INX motion
timing from `--assets`. It also preserves every source file with a hash and decoded text fields, when applicable, in `world_source_files`,
duplicate parsed definitions in `world_entity_variants`, and source references that cannot be resolved in
`world_import_unresolved_refs`. Preserving a source field does not by itself enable a gameplay feature; runtime behavior
is implemented in the shared simulation and application layers.

The import report lists missing item, monster, NPC, or asset references. Some entries in the current PTClassic snapshot
refer to files or codes absent from that snapshot. Inspect the report and `world_import_unresolved_refs` when
reconciling source data. Passing `--assets` is necessary to import animation timing; without it, the motion pass is
skipped.

## Validation and useful paths

```bash
dotnet run --project tests/PristonTale.Tests -c Release --no-build
git diff --check
```

The regression runner is a console program; `dotnet test` does not discover it. See [AGENTS.md](AGENTS.md) for
repository working rules. Source code for the database workflow is in
[M013Baseline.cs](Persistence/PristonTale.Persistence/Migrations/M013Baseline.cs) and
[PristonTale.WorldImport](tools/PristonTale.WorldImport/).

## License

The project's original source code is licensed under the GNU General Public License, version 3 only
(`GPL-3.0-only`). See [LICENSE](LICENSE).

Public forks and other distributed modified versions must keep the GPLv3 license and make the corresponding source
code available to their recipients. Distributing binaries also requires providing the corresponding source as
specified by GPLv3. Private forks and modifications used only internally do not have to be published. Running a
modified server for remote players, without distributing the server program, does not by itself trigger GPLv3's
source-sharing requirement.

This license does not grant rights to separately supplied game assets, original reference source, or third-party
dependencies. Those remain subject to their own terms.
