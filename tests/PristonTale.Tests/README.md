# Regression runner

Build in Release, with warnings treated as errors:

```bash
dotnet build tests/PristonTale.Tests -c Release -m:1 -warnaserror
dotnet run --project tests/PristonTale.Tests -c Release --no-build
```

The default runner uses synthetic data, original-protocol byte fixtures, a loopback TCP exchange and SQLite databases created and removed in the temporary directory.
It returns a nonzero exit code on failure. It is a console runner, not a test framework discovered by `dotnet test`.
No default case reads game assets, production databases or legacy reference directories.

Cases cover persisted attributes, session ownership, tick commands, validated movement reports, field transitions, invalid coordinates, AI integration,
inventory atomicity, original struct layouts, the packet cipher and replay window, and presence encoding through `WireRoundTrip`.
Rendering cases cover the photometric light model (the 18:00 sun, the night, the moonlight's colour and mesopic vision), the exposure calibration, the quality
presets and validation of the rendering options, the temporal jitter, the colour grade's cube files, the shadow
cascades' stability, the lamp shadow rows and the split-sum BRDF table.
Presentation cases cover combat events/rates, pending actor removal, unused material tables, shadow-caster eligibility, CPU mesh preparation, image decoding and glTF opacity variants.

Optional synthetic CPU benchmark:

```bash
dotnet run --project tests/PristonTale.Tests -c Release --no-build -- --benchmark
```

This runs five sets of 128 players, 100 warmup ticks and 1,000 measured ticks, reporting median/p95/max and bytes per tick. No assets, monsters, GPU or database participate.

Optional Ricarten probes, only when reading those assets is authorized:

```bash
dotnet run --project tests/PristonTale.Tests -c Release --no-build -- --profile-ricarten /path/to/assets
dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --profile-stream /path/to/assets /path/to/compiled/Content
```

The first reports CPU parsing/collision/texture preparation. The second creates a graphics device and uses the real incremental renderer uploader at a fixed 60 Hz.
Compile the client first for the shader content. Its Content directory normally resides under `Client/PristonTale/bin/Release/net10.0/Content`.
The graphics probe clears the window instead of drawing the complete world: timings isolate publication and do not establish in-game frame time or GPU capacity.
It also reports batch/triangle counts and a digest of static vertex data plus skinned bounds/palettes; this is not a readback of WriteOnly GPU buffers or a visual comparison.

Movement cases additionally cover matching prediction/authority endpoints, idle stability and collision rejecting the final step.
Monster navigation cases run fields on synthetic stages with walls: the leash and stuck recoveries, wall sliding, attack chaining, jams and attack range 0.
NPC service cases import synthetic NPC and name files (typed services, defaults, localized dialog, the placed file
winning over a same-name variant), check the shop and warehouse rules, round-trip the NPC messages, and run buy, sell,
repair, warehouse deposit, move, withdraw and gold, and teleport through the world tick with storage failures, stale
revisions, replays, conflicts and out-of-reach requests; the warehouse repository runs on a temporary SQLite database.
Network hardening cases cover the packet flood limit, login throttling, per-player command quotas and startup without field terrain.
Streaming policy covers the Forest 1 spawn prefetching Ricarten, speed-based lead distance and hysteresis.

GPU shader and render probes, after building the client content. They are MonoGame games on the Native platform
(Vulkan), so they need a Vulkan driver and a desktop session (a virtual X server without Vulkan presentation does not
do), and the runtime library the build copies next to the runner (README.md of the repository, Build). They read the
client's compiled content from the path given, whatever its location. `MG_HIDDEN_WINDOW=1` keeps their windows hidden;
it lengthens presentation by about 1.7 ms per frame on the GTX 1050 Ti, so timing runs keep the window visible.

```bash
dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --verify-shaders Client/PristonTale/bin/Release/net10.0/Content
dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --benchmark-render assets \
  Client/PristonTale/bin/Release/net10.0/Content /tmp/ricarten.json village-2 settings.json 15 2 12 true
```

`--verify-shaders` draws every technique of the static and skinned surface effects into a float target and checks the
material model against the same formulas in C#: emission, the masked cutout and the UV scroll, the second layer
(multiply, add), the lit model (ambient and the sun along the normal, a receiver in a shadow map), the distance fog
toward a flat colour and toward a sky capture, the shadow casters' depth (orthographic and perspective), skinning by a
bone translation, the selection rim, the sky layers, the display-referred output, the unlit effect and the composition
of an HDR scene through the post chain, the motion target (camera, jitter and a skinned bone's previous pose), the
automatic exposure (histogram and adaptation of a flat image), the bloom pyramid, the three display transforms and the
colour grade against their CPU references, the temporal resolve of a still scene and the tiled light culling (depth
range and side planes), the image-based lighting's irradiance scale, a lamp-lit actor behind an occluder (never black;
the inverse-square falloff), the vertex light on both paths (the albedo scaled in linear light, so animated scenery
matches the stage), the screen-space reflections of a scene whose rays all miss, foliage coverage (a
two-sided leaf whose texel alpha is above the cut-off covers the background exactly as an opaque texel does), a night
dome with a horizon glow and a moon disk (the reflections are never brighter than the visible dome, a dark rough ground
reflects no more of the sky, against its diffuse light, than under a uniform sky of the same irradiance, in the
irradiance's colour), mesopic vision through the final pass (a dim grey turns the night's hue, a day grey stays
neutral), and the volumetric light (scene x transmittance + in-scatter with the exact optical depth; 24 accumulated
frames of shafts through a finely striped shadow map deviate from the converged mean less than 40 % as much as one
frame does). The
default suite checks that leaves flagged to draw late stay opaque cut-outs of the depth pre-pass and that scenery
models take the stage's authored light for their texture. `--benchmark-render`
takes the settings file, seconds per run, runs, hour and camera orbit, optionally followed by camera distance, pitch, focus X/Z and camera yaw in radians
(0 looks toward +Z; pi/2 toward -X). The scene capture is written next to the report as `<report>.png`. Its
JSON reports frame, update, draw and present times, CPU time per render section (`CpuSectionsMs`), GPU time per pass
from Vulkan timestamps (`GpuSectionsMs`, through the MonoGame fork's `GpuTimestampPool`; `GpuTimestamps` is false when
the device lacks them; a frame that begins inside a render pass stays untimed) and the cost of each
incremental stage-upload step (`StreamingUploadSteps`). Section means count only frames in which the section ran;
`SkyCapture` is the atmospheric fog's sky copy, `SkyCaptureRefreshes` counts how often it was redrawn and
`DisplayTransform` is the final pass (exposure, display transform, grade). The post chain's sections are outside
`Frame`: `Temporal` (TAA resolve), `Exposure` (histogram and adaptation), `Bloom`, `DisplayTransform` and
`PostAntiAliasing` (FXAA or SMAA). Inside `Frame`: `SunShadow` (the cascades refreshed that frame), `LampShadows`,
`Prepass` (the depth pre-pass), `AmbientOcclusion` (GTAO, contact shadows and their blur), `LightCulling` and
`Environment` (the sky's cube faces, prefiltering and irradiance, four times a second), `Reflections` and `Volumetric`.
`EffectiveMultiSampleCount` is the HDR scene target's sample count (MSAA only).
The settings file must be a complete, valid client settings document with `maxFps` 0 and a windowed mode.

`--check-look` renders real scenes with the game data and fails on look regressions, writing its captures to the output
directory:

```bash
dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --check-look charselect assets   Client/PristonTale/bin/Release/net10.0/Content /tmp/look
```

`charselect` builds the character-select stage, braziers and a roster as the screen does and fails when more than 2 %
of the frame is clipped to white or more than 5 % has a clipped channel. `actor` draws NPCs at noon from the front and
from behind and compares the luminance of the two-pixel ring along their silhouettes with their interior (at most 1.25
times; no bright rim on actors that are not selected). `opening` plays the login screen's flythrough with its meteors
and fails when the town in the lower third of the frame is more than 1.8 times redder than it is blue (the sky there is
authored red). `scenery` puts a Ricarten metal texture on a stage quad with authored vertex light and on an animated
scenery model beside it (the skinned surface path, as actors), at noon and 17:00, and fails when their luminance
differs by more than 8 %, their saturation by more than 0.05, or the animated quad's specular light (the `Specular`
debug view) exceeds the stage quad's by more than 25 % (no chrome on a non-metal). `shade` stands an NPC on a floor in
the open and on a floor whose authored light is dark (a tree's shade) at 10:00, and fails when the actor's luminance
in the shade over the open differs from the floor's own ratio by more than a factor of 1.5.

The streaming probe uses the same 32-step/2 ms policy as the game; `--profile-stream-baseline` uses eight steps to isolate the previous limit.
Authentication and job-head coverage uses presentation probes in this runner.
HUD cases cover the original 800x600 layout at integer scales, its bottom/right anchors and hit rectangles, gauge cropping,
the panel slide steps, minimap clipping and the Sinimage color keys (black key for BMP, premultiplied TGA alpha).
The optional HUD probe renders the original in-game HUD scenarios, including scale 2 and 3, to PNG files:

```bash
dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --capture-hud assets Client/PristonTale/bin/Release/net10.0/Content /tmp/hud
```

The `npc-*` scenarios show the typed NPC dialog, teleport destinations, the shop (buy and sell modes) and the warehouse.
It reads the Ricarten stage only for the authored minimap rectangle and accepts an optional background PNG as a fifth argument.
The target probe draws Ricarten at 14:00 with a monster of two races, an NPC and a player spawned through the remote
entity roster, and captures the HUD with the target frame's live portrait, the classic bar, 1280x720, 1920x1080, scale 2
and the minimap under the cursor:

```bash
dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --capture-hud-target assets Client/PristonTale/bin/Release/net10.0/Content /tmp/hud-target
```

The login probe exercises Draw before Update with a preloaded shared atlas, a 1×1 viewport followed by restoration, disabled credentials, keyboard navigation, masking and scaled mouse hit tests.
The default suite covers zero/tiny viewport overlay bounds and all 416 normal job heads against `Fixtures/job-head-source.json`, extracted from the original client's job-head table (rows 0..3).

Skill probes. `--verify-skill-motions` checks the motion index, length, event frames and class tag of every class
skill rule (and its per-rank motion variants) against the class motion tables in the game data. `--capture-skills`
casts Fire Ball, Healing, Pike Wind, Extreme Shield and Impact, or the skills given as a fifth argument
(comma-separated hex codes such as `0x05040004,0x07080003`), through the client's skill presentation with the server's
timeline and writes the frames; it fails when a cast starts no motion or shows no effect light. `--filter <text>` runs
only the regression cases whose name contains the text:

```bash
dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --verify-skill-motions assets
dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --capture-skills assets Client/PristonTale/bin/Release/net10.0/Content /tmp/skills
```
# Regressões de terreno e transição

O runner inclui pisos sobrepostos, rampas, buracos, degraus altos, frames longos e corrida entre campos pela autoridade compartilhada.
Também verifica colisão da câmera entre campos, contato físico, câmera acima de portal/poste, foco em várias frequências, reposicionamento, botão do meio e rotação pelas bordas com captura pela UI/foco da janela.
O probe opcional lê somente o stage original de Ricarten, sem GPU, para verificar pisos sobrepostos e câmeras altas com o foco encoberto próximos ao portão:

```bash
dotnet run --project tests/PristonTale.Tests -c Release --no-build -- --inspect-ricarten-ground assets
```
