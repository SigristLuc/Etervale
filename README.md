# Etervale

Etervale is a C# and MonoGame reimplementation of Priston Tale. Gameplay authority runs on the game server.
The server uses SQLite by default; PostgreSQL remains an option.

The game is online only. The C# client and server speak the original Priston Tale TCP protocol of the English C++
client on port 10009: frame header, packet cipher, opcodes and 32-bit struct
layouts. The server stays authoritative, and inventory, progression and resolved combat use Etervale extension opcodes.
See the [protocol notes](Shared/PristonTale.Protocol/README.md) for the wire contract, its deviations and server capacity.

This README covers the local setup, including the world database migration and data import. Commands below run
from the repository root and use the Release configuration.

## Requirements

- .NET 10 SDK.
- [Game data archive](https://drive.google.com/file/d/1-uiMsGgfICPkTvObkp80vsnYp1PH8v5N/view?usp=drive_link).
  Download and extract it at the repository root. Place authored configuration in `assets/GameServer/`.
- For the client: a GPU and driver with Vulkan 1.1 or later. The client uses MonoGame's Native platform on Vulkan
  through the fork in [monogame/](monogame/README.md); shaders compile with DXC on every platform (no Wine).
- The client's native runtime, built once per machine (see Build): on Windows, Visual Studio 2022 or 2026 with the
  "Desktop development with C++" workload, and Git; on Linux, cmake, make, a C++17 compiler, premake5, pkg-config, Git
  and the ALSA, PulseAudio and PipeWire development headers. The Vulkan SDK is not needed.
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

Build the MonoGame native runtime (`mgruntime`: Vulkan, SDL2, FAudio) once per machine, and again after any change
under `monogame/runtime/src`. It is written to `monogame/runtime/bin/<rid>/`, which is git-ignored; the client and the
regression runner copy it next to their executables, and their builds stop with these instructions when it is missing.

```powershell
# Windows (PowerShell): SDL2 and FAudio at the pinned commits, premake, MSBuild; about 5 minutes the first time.
powershell -ExecutionPolicy Bypass -File monogame/runtime/build-windows.ps1
```

```bash
# Linux and macOS.
monogame/runtime/build.sh
```

The Linux script is the fork's own and is used unchanged; see [monogame/runtime/README.md](monogame/runtime/README.md)
for its packages. Then build the solution and the importer:

```bash
dotnet tool restore
dotnet restore PristonTale.sln -warnaserror
dotnet build PristonTale.sln -c Release -m:1 -warnaserror
dotnet build tools/PristonTale.WorldImport/PristonTale.WorldImport.csproj -c Release -m:1 -warnaserror
```

`dotnet tool restore` installs the MonoGame content builder (`dotnet-mgcb`) of `.config/dotnet-tools.json`; the client
build compiles its shaders with the fork's effect processor (`monogame/pipeline`), which the solution builds first. The
standalone importer build is explicit because its Release output must be available for the `--no-build` commands below.

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

### Client rendering settings and debug keys

The client stores its settings in `%LOCALAPPDATA%/PristonTale/client-settings.json` (the local application data folder
on other systems). Rendering fields and their defaults:

| Field | Default | Notes |
|---|---|---|
| `multiSampleCount` | `4` | MSAA samples of the HDR scene target: 0, 2, 4 or 8; the Vulkan runtime caps it at 4. |
| `shadowMapSize` | `4096` | Sun shadow map: 1024, 2048, 4096 or 8192. If the GPU cannot allocate it, the renderer falls back to half the size, down to 1024; 2048 is the low tier. |
| `lampShadowTileSize` | `512` | Cube face size of the lamp shadows that actors cast: 256, 512 or 1024 (see the rendering options below). |
| `shadowUpdateRate` | `60` | Shadow refresh rate in Hz; 0 refreshes every frame. |
| `distanceFog` | `"Atmospheric"` | Beyond 2208 units `Atmospheric` fades toward the visible sky (at most 85%, warmer toward the sun); `Off`. `Classic` (the old flat clip) draws as `Atmospheric`; `true`/`false` mean `Atmospheric`/`Off`. |
| `debugView` | `"Shaded"` | Development view; the values are listed with the rendering options below. |
| `monitor`, `refreshRate` | `-1`, `0` | The Vulkan platform reports the primary monitor's modes only and switches fullscreen at the desktop frequency, so other monitors and rates are not selectable. |

The renderer draws the world in HDR (linear radiance, a HalfVector4 target) with one physically based material model
fed by the legacy materials, then applies the display transform (Khronos PBR Neutral) before the HUD; see
[Content/Shaders/README.md](Client/PristonTale/Content/Shaders/README.md).

Existing settings files keep their stored values; the defaults apply to new files and missing fields.
In game, `Ctrl+F3` toggles the render statistics in the window title, `Ctrl+F5` toggles shadows and `Ctrl+F10` cycles
the debug view.

Log in with the development account you created. The client connects to `tcp://localhost:10009`; the server
listens on TCP port 10009. The server also applies pending migrations at startup, but it does **not** run the
world importer. Import before starting the server so it loads populated catalogs. A migration or catalog load failure
stops server startup. The server also reads every field's stage `.smd` from `assets/` (found next to the working
directory or set with `Assets:Root`) and keeps its collision in memory. A missing assets directory or stage stops
startup too; for tests or development without assets, pass `--Assets:AllowMissingTerrain=true` to run those fields
without terrain checks.

### Server settings

Settings come from `Server/PristonTale.Server/appsettings.json`, environment variables (`World__ExperienceMultiplier`)
or the command line (`--World:ExperienceMultiplier=1`). A network limit of `0` disables it.

| Setting | Default | Meaning |
| --- | --- | --- |
| `World:ExperienceMultiplier` | 5 | Etervale's server-wide bonus on monster experience; the original had no fixed bonus |
| `World:MaxPendingCommandsPerPlayer` | 64 | Commands one player may have waiting for the world tick |
| `Network:PacketLimit` / `Network:PacketWindowSeconds` | 251 / 5 | Disconnect above this many packets per window, as the original |
| `Network:MaxConnectionsPerIp` | 8 | Open connections per remote address |
| `Network:LoginTimeoutSeconds` | 30 | Close connections that have not logged in (checked every 5 s) |
| `Network:LoginFailuresPerAccount` / `Network:LoginFailuresPerIp` | 5 / 20 | Failed logins before further attempts are refused |
| `Network:LoginFailureWindowSeconds` | 300 | Window for the failed-login counts |
| `Network:PlayerInfoBurst` / `Network:PlayerInfoPerSecond` | 512 / 128 | `GETPLAYERINFO` replies per connection |

`GETPLAYERINFO` does not count toward the packet limit; requests beyond its bucket are dropped. Rejected packets and
refused connections are logged at most once per interval with a count of the suppressed lines.

The `import` command applies the schema migration before reading source files. `verify` also applies pending migrations,
then prints catalog counts and reference checks. Repeating `import` updates the catalog without deleting accounts or
characters. To create an empty schema, run `verify` against a new database with `--sqlite` or `--postgres`.

For PostgreSQL, start `docker compose up -d --wait`, use `--postgres "<connection string>"` with the importer, use
`--conn "<connection string>"` with `PristonTale.DevAccounts`, and set `Database__Provider=Postgres` plus
`ConnectionStrings__Postgres` for the server. `docker compose down` keeps the named volume; `docker compose down -v`
deletes that volume and its data.

### Client settings

The client keeps its options in `client-settings.json` under the local application data folder
(`%LOCALAPPDATA%\PristonTale` on Windows) and changes them from the in-game system menu, entry Settings. Besides the
display, audio and shadow options, `classicTargetHealthBar` selects the target frame's HP bar: `false` (default) always
shows the bar with a percentage; `true` follows the original rule and shows the bar, without numbers, only after you
attack a monster at full life.

Rendering options (all validated on load; invalid values fall back to the defaults below). The in-game Settings panel
cycles the preset (`Graphics quality`), the anti-aliasing, the sun shadow size and refresh and the shadows; the other
options are edited in the file.

| Option | Values (default) | Effect |
|---|---|---|
| `quality` | `Low`, `Medium`, `High` (default), `Ultra`, `Custom` | Preset of the options below; `Custom` once one is changed by hand. |
| `antiAliasing` | `Off`, `Fxaa`, `Smaa`, `Msaa`, `Taa` (default) | `Taa`: jittered frames, motion vectors (skinned actors from their previous pose) and a history; `Msaa` multisamples the HDR scene with `multiSampleCount` (2 or 4); FXAA and SMAA 1x filter the display image. |
| `autoExposure` | `true` (default), `false` | Luminance histogram and adaptation on the GPU; `false` uses the light model's incident reading. |
| `exposureCompensation` | -3 to 3 stops (0) | Added to the automatic or fixed exposure. |
| `tonemapper` | `PbrNeutral` (default), `AgX`, `Aces` | Display transform of the final pass. |
| `bloom` | `Off`, `Low`, `High` (default) | Bloom pyramid mixed at 3 % or 5 %. |
| `colorGrading`, `colorGradingLut` | `true`, `""` | Built-in colour grade (3D LUT), or the `.cube` file named by `colorGradingLut`. |
| `sharpening` | 0 to 1 (0.35) | Contrast-adaptive sharpening of the final image. |
| `localLightLimit` | 8, 16, 32 (32) | Stage lamps and combat lights shaded per pixel, nearest to the camera focus first. |
| `shadowMapSize`, `shadowUpdateRate`, `shadowsEnabled` | 1024 to 8192 (4096), 0/30/60/120 Hz (60), `true` | Atlas of the sun's (and moon's) shadow cascades, its refresh rate, all shadows. |
| `shadowCascades`, `shadowFilter` | 1 to 4 (4), `Pcf` or `Pcss` (default) | Stabilized cascades to 9000 units; PCSS softens the penumbra with the distance to the occluder (the moon uses PCF). |
| `ambientOcclusion`, `contactShadows` | `true`, `true` | GTAO and screen-space contact shadows at half resolution from the depth pre-pass (not with MSAA). |
| `lampShadowCount`, `lampShadowTileSize` | 0, 2, 4 (4); 256, 512 (default), 1024 | Shadows that actors cast from the nearest stage lamps (cube faces of this size). |
| `imageBasedLighting` | `true` | Irradiance (spherical harmonics) and prefiltered reflections from the sky dome, refreshed four times a second; `false`: a hemispherical sky/ground ambient. |
| `screenSpaceReflections` | `false` (Ultra: `true`) | Screen-space reflections of water and ice over the sky's reflection (with the depth pre-pass). |
| `volumetricLight` | `true` (Low, Medium: `false`) | Sunlight scattered by a height fog, with the shafts the shadows cut, at quarter resolution (by day). |
| `calibrationSpheres` | `false` | Look development: an 18 % grey and a chrome sphere beside the camera focus. |
| `distanceFog` | `Off`, `Classic`, `Atmospheric` (default) | Distance fog toward the captured sky. |
| `debugView` | `Shaded` (default), `Albedo`, `VertexColour`, `Normals`, `Lighting`, `Overdraw`, `ExposureFalseColour`, `Roughness`, `Metallic`, `AmbientOcclusion`, `Specular` | Development views, also cycled with Ctrl+F10 (`AmbientOcclusion`: occlusion in red, contact shadows in green; `Specular`: the specular light alone). |

| Preset | Anti-aliasing | Sun shadow | Occlusion | Lamp shadows | Bloom | Lights | GPU at 1920x1080, GTX 1050 Ti |
|---|---|---|---|---|---|---|---|
| `Low` | off (FXAA costs 0.7 ms) | 2048, 2 cascades, PCF, 30 Hz | off | off | off | 8 | about 2.6 ms (day), 3.4 ms (night) |
| `Medium` | SMAA | 2048, 3 cascades, PCF, 60 Hz | GTAO | 2 x 512 | High | 16 | about 3.6 ms, 4.8 ms |
| `High` | TAA, sharpening 0.35, volumetric light | 4096, 4 cascades, PCSS, 60 Hz | GTAO, contact | 4 x 512 | High | 32 | about 5.7 ms, 5.7 ms |
| `Ultra` | as High, screen-space reflections | 4096, 4 cascades, PCSS, every frame | GTAO, contact | 4 x 1024 | High | 32 | High plus the shadows every frame and the reflections |

The GPU times are the frame totals of `--benchmark-render` in Ricarten at noon and in the lamp-lit street at 21:00
(`tests/PristonTale.Tests/README.md`), with the shadow cascades refreshed at 60 Hz; at 60 frames per second or less
every frame refreshes them (about 1 ms more on High).

The scene is lit in photometric units: sun, sky and moon illuminance from the time of day, the stage lamps and the
night levels from their authored light levels, and the camera exposure in EV100 (incident calibration by day, the
histogram's reflected reading, a dimming curve for dusk and night). The low sun is reddened by the air it crosses; the
stage lamps fall off with the inverse square of the distance from a shared reference distance, windowed to zero at their
range. Combat and spell lights by day add only a small share of the daylight. Local lights are culled per 16x16-pixel
tile on the GPU. Alpha-tested foliage, also the leaves flagged to draw late, is an opaque cut-out of the depth pre-pass:
it has its own depth, motion and occlusion, so the temporal anti-aliasing and the screen-space passes do not show the
scene behind the leaves through them. Stage geometry is tinted by its authored per-vertex light; the scenery models on
a stage (windmills, cranes, flags, static or animated) take the stage's light for the same texture, so they match the
stage beside them; actors take the authored light of the floor under their feet, so they darken with it in a tree's
shade or an alley (except on the character-select stage). The authored light also occludes the sky's reflections, and
the sky's irradiance is windowed so curved surfaces show no bright and dark bands. `Client/PristonTale/Content/Shaders/README.md`
describes the passes.

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

The import report lists missing item, monster, NPC, or asset references. Some entries in the current `GameServer` snapshot
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
`PristonTale.DevAccounts create-bots <prefix> <count> <password>` against a copy of the database and start the server
with `--Network:MaxConnectionsPerIp=0`, since every bot connects from one address. See
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
