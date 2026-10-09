# Etervale

Etervale is a C# and MonoGame reimplementation of Priston Tale. Gameplay authority runs on the game server.
The server uses SQLite by default; PostgreSQL remains an option.

The game is online only. The C# client and server speak the original Priston Tale TCP protocol of the English C++
client in `original_references/src` on port 10009: frame header, `EncodePacket` cipher, opcodes and 32-bit struct
layouts. The server stays authoritative, and inventory, progression and resolved combat use Etervale extension opcodes.
See the [protocol notes](Shared/PristonTale.Protocol/README.md) for the wire contract, its deviations and server capacity.

This README covers the local setup, including the world database migration and data import. Commands below run
from the repository root and use the Release configuration.

## Requirements

- .NET 10 SDK.
- [Game data archive](https://drive.google.com/file/d/1-uiMsGgfICPkTvObkp80vsnYp1PH8v5N/view?usp=drive_link).
  Download and extract it at the repository root. Place authored configuration in `assets/GameServer/`.
- A working graphics environment for the client. On Linux, shader compilation also requires Wine and a MonoGame MGFXC
  Wine prefix; set `MGFXC_WINE_PATH` to that prefix when building.
- Docker with Compose only if you choose PostgreSQL for the online server.

The game assets and authored world data are excluded from Git. `assets/` contains models,
textures, audio, HUD resources, and INX animation data; `assets/GameServer/` contains the authored item, monster, NPC, and field
configuration read by the importer. The asset archive must include `assets/Hud/Fonts/PristonUiSans-Regular.otf` for the
MonoGame content build. `assets/Hud/` also supplies the login and display-confirmation frames; the in-game HUD draws the
original sprites from `assets/image/Sinimage/`, `assets/image/` and the field maps in `assets/Field/map/`.

The asset snapshot available during the September 2026 review has no `assets/Hud/` directory or that font. The server
and importer run, but a clean client build or launch still requires those interface resources or a port of the original
sprites and bitmap fonts.

Expected layout:

```text
Etervale/
├── assets/
│   ├── char/
│   ├── Field/
│   ├── Effect/
│   ├── image/
│   ├── sky/
│   ├── wav/
│   ├── Hud/
│   └── GameServer/
│       ├── OpenItem/
│       ├── Monster/
│       ├── NPC/
│       └── Field/
└── data/
```

The paths above are defaults, not hardcoded requirements for the importer. The importer looks for `assets/GameServer/` or `GameServer/` in the
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

## Screenshots

<img width="1918" height="1012" alt="image" src="https://github.com/user-attachments/assets/2850e4b8-9c72-43b2-9171-6434a6313135" />
--
<img width="1918" height="1012" alt="image" src="https://github.com/user-attachments/assets/81668a53-e1ed-499d-b5d5-e13488e596dd" />
--
<img width="1918" height="1012" alt="image" src="https://github.com/user-attachments/assets/ac1b25ce-96f1-489f-9aa4-a2c9f6e70fb1" />




## Online: migrate, import, and connect

Run these commands from the repository root. The server, importer, and account tool use the same SQLite file at
`data/pristontale.db`. The importer applies `M013Baseline` before importing. It preserves accounts and characters on
repeat runs. SQLite runs in WAL mode, so `-wal` and `-shm` files appear next to the database while it is in use.

```bash
dotnet build tools/PristonTale.DevAccounts/PristonTale.DevAccounts.csproj -c Release -m:1 -warnaserror

dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- import \
  --sqlite data/pristontale.db \
  --source assets/GameServer \
  --assets assets

dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- verify \
  --sqlite data/pristontale.db

dotnet run --project tools/PristonTale.DevAccounts -c Release --no-build -- create-account demo change-me
dotnet run --project Server/PristonTale.Server -c Release --no-build
```

Run the client in another terminal:

```bash
dotnet run --project Client/PristonTale -c Release --no-build
```

Log in with the development account you created. The client connects to `tcp://localhost:10009`; the server
listens on TCP port 10009. The server also applies pending migrations at startup, but it does **not** run the
world importer. Import before starting the server so it loads populated catalogs. A migration or catalog load failure
stops server startup. The server also reads every field's stage `.smd` from `assets/` (found next to the working
directory or set with `Assets:Root`) and keeps its collision in memory; without the assets it starts but skips terrain
checks.

The `import` command applies the schema migration before reading source files. `verify` also applies pending migrations,
then prints catalog counts and reference checks. Repeating `import` updates the catalog without deleting accounts or
characters. To create an empty schema, run `verify` against a new database with `--sqlite` or `--postgres`.

For PostgreSQL, start `docker compose up -d --wait`, use `--postgres "<connection string>"` with the importer, use
`--conn "<connection string>"` with `PristonTale.DevAccounts`, and set `Database__Provider=Postgres` plus
`ConnectionStrings__Postgres` for the server. `docker compose down` keeps the named volume; `docker compose down -v`
deletes that volume and its data.

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

The supplied `assets/GameServer` snapshot includes item categories absent from the original reference DWORD table.
The importer preserves those rows with `item_code_mask=0` and reports their categories; the runtime omits them until
their numeric codes are verified. This avoids assigning conflicting item identities.

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

With the server running, `tools/PristonTale.SmokeClient` checks login, character creation, world entry and presence
over the protocol. `tools/PristonTale.LoadTest` drives headless bots; create their accounts with
`PristonTale.DevAccounts create-bots <prefix> <count> <password>` against a copy of the database. See
the [server capacity notes](Shared/PristonTale.Protocol/README.md#server-capacity).

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
