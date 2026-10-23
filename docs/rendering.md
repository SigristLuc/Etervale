# Rendering (Engine3D)

`Client/PristonTale.Engine3D/`: the legacy model, stage, animation and camera-path readers, asset path resolution,
client collision queries, the HDR renderer (scene, lighting, shadows, sky, post chain) and its diagnostics. Shader
sources are in [shaders.md](shaders.md); option fields in [client-settings.md](client-settings.md).

## Read this when

- Changing pass order, a render target, the light model, exposure, shadows, fog, IBL, SSR, volumetric light or AA.
- A surface is too bright/dark, shimmers, ghosts under TAA, or loses its shadow or occlusion.
- Loading a new legacy asset kind, or a stage/model/texture fails to load or looks wrong.
- Field streaming hitches (GPU upload steps) or a frame costs too much (statistics, profiler, `--benchmark-render`).
- Adding something that draws into the HDR scene (effects, portraits, tools).
- Touching the boundary with the MonoGame Vulkan fork.

## Code map

| Path | Responsibility |
|---|---|
| `Formats/PristonSmdReader.cs` | Binary stage and model files (`.smd` stage, `.smd`/`.smb` model and skeleton): header kinds, material group, vertices, faces, texture links, lights, area index, animation keys. |
| `Formats/PristonInxReader.cs` | Model-info files (`.inx`): mesh and motion file names, LOD groups, motion table, talk motions. |
| `Formats/PristonActorLoader.cs` | Builds a `PristonActor` (mesh + skeleton + motions) from an `.inx`, or a skinned/rigid model without one. |
| `Formats/PristonCameraPath.cs` | Cinematic camera paths (`.cam`) of the opening. |
| `Formats/PristonFormats.cs` | Format records (`PristonStage`, `PristonModel`, `PristonMaterial`, `PristonStageLight`, motions) and `PristonFormatConstants`. |
| `Formats/BinaryUtil.cs`, `PristonAssetCache.cs` | Bounds-checked `BinaryCursor`; case-insensitive cache of parsed stages and models. |
| `IO/PristonFileResolver.cs` | Case-insensitive path resolution for original asset names (use it for every original asset path). |
| `IO/ClientSettingsStore.cs` | `client-settings.json` persistence ([client-settings.md](client-settings.md)). |
| `IO/PristonWaveDecoder.cs` | WAV decoding for the audio library. |
| `Physics/` | `PristonCollisionWorld`: client view of the shared `CollisionMesh` (`Shared/PristonTale.Geometry`) built from the stage faces flagged for collision; floor height and segment traces. |
| [`Rendering/PristonRenderer.cs`](../Client/PristonTale.Engine3D/Rendering/PristonRenderer.cs) | The renderer: stage/actor residency, animation, light selection, frame graph (`Draw`), post chain (`ComposeScene`). `PristonRenderer.Portrait.cs`: actor portraits with a studio light. |
| `Rendering/PristonSceneTargets.cs` | HDR scene, motion, pre-pass targets; sky composition; caller target capture and restore. |
| `Rendering/PristonShaderLibrary.cs` | Surface effects, frame constants, technique selection. |
| `Rendering/PristonMaterial*.cs`, `PristonMaterialAdapter.cs` | Legacy material to runtime material: queue classification, textures and layers, roughness/reflectance/shading model. |
| `Rendering/PristonTextureLoader.cs`, `PristonImageDecoder.cs`, `PristonMipChain.cs` | Texture decode off-thread, mip chain with alpha-coverage preservation, GPU upload. |
| `Rendering/RenderBatch.cs`, `RenderStageHandle.cs`, `RenderQueueBuilder.cs`, `PristonRenderQueue.cs`, `BatchUploader.cs`, `PristonGpuMesh*.cs` | Batches, per-stage residency, culling and sorting, dynamic-buffer uploads. |
| `Rendering/PristonModelAnimator.cs`, `AnimatedModelRuntime.cs`, `PristonSkinPalette.cs`, `PristonModelNormals.cs`, `PristonMotionSelection.cs` | Keyframe evaluation, animated scenery, bone palettes (current and previous pose), rebuilt normals, motion clip choice. |
| `Rendering/PristonCamera*.cs` | Orbit camera, collision, field-of-view limits. |
| `Rendering/PristonDayNightCycle.cs`, `DayNightSettings.cs` | Time of day, sun/moon direction and illuminance, sky colour, lamp factor, night hue. |
| `Rendering/Lighting/` | Photometry and exposure, stage baked light and floor light field, shadow cascades, lamp shadows, light culling, transient lights, environment lighting, volumetric light, calibration spheres. |
| `Rendering/Post/` | Post chain (`PristonPostProcessor`), auto exposure, bloom, colour grade, display transform CPU reference, GTAO/contact shadows, SSR, TAA, SMAA (`Smaa/` tables and credits), full-screen triangle. |
| `Rendering/Sky/` | Sky manifest (layers per profile), sky model library, profile selection, the fog's sky capture. |
| `Rendering/GlowRenderer.cs`, `PristonUnlitEffect.cs`, `PristonVfxEffect.cs`, `PristonMeshEffect.cs` | Lamp coronas and the effect shaders other projects draw with. |
| `Rendering/ClientSettings.cs`, `GraphicsOptions.cs`, `DistanceFogMode.cs`, `PresentationSettings.cs` | Options and the change event the renderer listens to. |
| `Diagnostics/` | `PristonFrameProfiler` (CPU and GPU timestamps per `RenderSection`), `PristonStreamingStats`, `FrameTiming`, `FramePacer`, `IClientLog`. |

## MonoGame fork

The client runs MonoGame's Native platform on Vulkan through the fork in `monogame/` (framework from source, native
runtime `mgruntime`, SM 6 effect processor); see [monogame/README.md](../monogame/README.md). Engine3D references
`monogame/framework/MonoGame.Framework/MonoGame.Framework.Native.csproj`. Engine3D relies on fork features: compute
effects and `StructuredBuffer` (light culling, exposure, SH), `GpuTimestampPool` (profiler), resolve of multisampled
float targets. Display limits of the platform (primary monitor only, desktop refresh rate) are handled in
`Client/PristonTale/Display/DisplayController.cs`. Fork changes go into `monogame/` with its own issue notes, not here.

## Threading and lifecycle

- All GPU work and every `PristonRenderer` member run on the main (game) thread. `Update` advances time of day,
  transient lights, sky-profile blending (2 s cross-fade) and animation; `Draw` renders the scene.
- Callers draw in this order (`Client/PristonTale.Gameplay/World/GameScene.Draw`): `PristonRenderer.Draw(camera)` ->
  effects that add to the HDR scene (monster mesh effects, return effect, legacy particles, skill VFX) ->
  `PristonRenderer.ComposeScene()` -> UI in sRGB. The HDR scene stays bound between `Draw` and `ComposeScene`.
- Asset preparation is off-thread: `PrepareStageTexturesAsync` / `PrepareActorResourcesAsync` decode textures (two
  decode slots) and build actor geometry on the thread pool; the field streamer
  (`Client/PristonTale.Gameplay/World/Streaming/FieldResourceStreamer.cs`) awaits them.
- GPU upload is incremental on the main thread: `AddStageIncrementally` returns an enumerator; each `MoveNext` does one
  indivisible step (one texture upload, one mesh upload, a material setup, or a poll of a worker task) and reports it
  to `PristonStreamingStats`. The streamer advances it within `FieldStreamingPolicy.UploadBudgetMilliseconds` (2 ms) and
  at most 32 steps per frame. The stage publishes (joins `_stages`) only at the end; an abandoned enumerator disposes
  its GPU resources.
- `RequirePreparedTextures()` (set by `GameScene`) makes an unprepared texture upload throw instead of decoding
  synchronously on the main thread. The synchronous `AddStage`/`Load` path is for loading screens and tools.
- Settings: `PresentationSettings.RenderingChanged` calls `ApplyQuality` on the main thread at the frame boundary; it
  may reallocate the sun shadow atlas (releasing the old one first) and reload the colour-grade LUT from disk.
- `ResetTemporalState()` after camera cuts and teleports: drops TAA history and snaps exposure.

## Legacy formats

Described in Etervale's terms; readers keep every field, including padding, for round-trip checks.

| Format | Reader | Contents and conventions |
|---|---|---|
| Stage `.smd` | `PristonSmdReader.ReadStage` | Header string `SMD Stage data Ver 0.72` (also 0.73), material group, vertices (fixed-point position, BGRA 16-bit colour = authored light), faces (three indices, material, texture-link pointer), texture links (per-corner UVs, chained for extra layers), lights (28-byte records: type flags, position, range, colour), the area index, a source trailer. Positions convert as (x, y, -z). |
| Model `.smd`, skeleton `.smb` | `PristonSmdReader.ReadModel` | Header `SMD Model data Ver 0.62` or a binary model layout; objects with parent names, base transform, rotation/position/scale keys and frame ranges; physique bone per vertex. Positions convert as (x, z, -y). |
| Model info `.inx` | `PristonInxReader` | Mesh, motion and sub-model file names, high/default/low LOD object groups, 512 motion entries of 172 bytes, talk and NPC motions; two layouts differ only in the item-code list width. |
| Camera path `.cam` | `PristonCameraPath.ReadCam` | Position and rotation keys of the opening cinematic, 160 ticks per frame. |
| Textures `.bmp`, `.tga`, `.jpg` | `PristonImageDecoder` | BMP (always opaque; a variant stores its first 14 header bytes with i*i added to byte i), TGA (alpha), others through StbImageSharp; a missing extension is retried as `.bmp`, `.tga`, `.jpg`, `.jpeg`. |
| Sky manifest | `PristonSkyManifest` | Latin-1 text: profiles in braces, layer lines `FileName: ... FadeDelay: ... RGBA: r g b a`. Profile numbers come from field backdrop codes (`PristonSkyProfiles.Resolve`). |

- Fixed-point coordinates divide by `PristonFormatConstants.FixedScale` = 256. Distances are in Priston units (a map
  cell is 64 units).
- Animation time is in ticks of 160 per frame. Stage props advance 2000 ticks per second and start one frame in
  (160); actor motions use 160000/33 ticks per second scaled by the motion's playback rate.
- Materials: a TGA or opacity-mapped texture is a cut-out (alpha test at 60/255). Legacy blend types 2-6 draw late
  (blended queue) unless the material is an opaque cut-out; one layer above the base is carried over (multiply, add or
  sphere-map shine); there is no roughness or metalness in the data (`PristonMaterialAdapter` assigns 0.85 rough
  dielectric, water 0.12, ice 0.3, shine 0.4).

## Scene data

- A stage becomes a `RenderStageHandle`: static batches (merged by material when it has no scroll, wind, layers or
  transparency), animated scenery groups sharing bone palettes, a static shadow aggregate per grid cell, the stage's
  lights split into ground lights and glow (corona) lights, `PristonStageBakedLight` and `PristonStageLightField`.
- Actors (`AddActor`) get GPU resources shared per `PristonActor`, a bone palette (at most 64 bones,
  `PristonShaderLibrary.MaxBones`), rebuilt normals, and can be attached to a parent bone (`AttachActor`).
- `RenderQueueBuilder` frustum-culls batches by bounds and sorts: opaque and alpha-tested by state key then front to
  back, transparent back to front, shadow casters by state key. Shadow casters are collected before culling.
- Queues: `Opaque`, `AlphaTest`, `Transparent` (`PristonMaterialRuntimeFactory.Classify`).

## Frame graph

`PristonRenderer.Draw`, then `ComposeScene`. Each row is a `RenderSection` of the profiler.

| # | Section | Runs when | Work |
|---|---|---|---|
| 1 | `Lighting` | always | Day/night state, photometric light, exposure inputs; transient lights then nearest lamps up to `localLightLimit`. |
| 2 | `Queue` | always | Culling and sorting; shadow casters when a sun or lamp shadow redraws this frame. |
| 3 | `SunShadow` | sun shadows on and the refresh scheduler fires | Cascades of this refresh into their atlas tiles. |
| 4 | `LampShadows` | shadows on, lamps lit, rows to redraw | Cube faces of new lamp rows and one other row in turn. |
| 5 | `SkyCapture` | fog on, sky visible, sky changed | Four 32-px tiles of the dome for the fog colour. |
| 6 | `Environment` | IBL on, every 250 ms | Six dome faces, SH irradiance, octahedral map, prefiltered atlas. |
| 7 | `Sky` | always | Dome layers into the 8-bit sky target, then `SkyComposite` fills every scene target. |
| 8 | `Prepass` | no MSAA, not `Overdraw` | Depth, linear depth, normals (smoothness in alpha), motion of opaque surfaces and cut-outs. |
| 9 | `AmbientOcclusion` | pre-pass ran and AO or contact shadows on | GTAO and contact shadows at half resolution, blur. |
| 10 | `LightCulling` | any local light | 16x16 tile masks (depth-bounded after the pre-pass). |
| 11 | `Opaque`, `Masked` | always | Lit pass; after the pre-pass it tests depth without writing it. Without the pre-pass, TAA motion is written here. |
| 12 | `Reflections` | pre-pass, SSR on, a surface with roughness < 0.3 seen | Scene copy and screen-space reflections. |
| 13 | `Transparent` | always | Blended queue, back to front. |
| - | (calibration) | `calibrationSpheres` | Grey and chrome sphere through the lit pass. |
| 14 | `Glow` | always | Coronas of glow lights and glow-emitter props, scaled by the lamp factor. |
| 15 | `Volumetric` | pre-pass, volumetric on, sun shadows on, sun up | March, accumulate, composite over the scene. |
| - | effects | caller | Particles, VFX, mesh effects into the bound HDR scene. |
| 16 | `Temporal` | TAA | Resolve into history (`ComposeScene`). |
| 17 | `Exposure` | auto exposure | Histogram and adaptation, no read-back. |
| 18 | `Bloom` | bloom not `Off` | Six-level pyramid. |
| 19 | `DisplayTransform` | always | Final pass into the caller's target (or an 8-bit target for FXAA/SMAA). |
| 20 | `PostAntiAliasing` | FXAA or SMAA | Filter into the caller's target. |

Mode switches: `Taa` and `Msaa` are exclusive; MSAA (2-4 samples) disables the pre-pass and therefore AO, contact
shadows, SSR, volumetric light, soft particles and depth-bounded tiles. `Overdraw` disables TAA, the pre-pass, the sky
and IBL and draws everything additive. `Underground` removes sun, sky dome and shadows and lights with a fixed ambient
and lamps at full.

## Render targets

| Target | Format | Notes |
|---|---|---|
| Scene colour + depth | HalfVector4, Depth24 | Pre-exposed linear radiance; MSAA when `Msaa`; `PreserveContents`, filled by `SkyComposite`. |
| Motion | HalfVector4 | TAA only: uv motion, previous view depth, view depth (negative on cut-outs). |
| Linear depth, normals | R32F, RGBA8 | Pre-pass only; `SceneLinearDepth` exposes the depth to effects (null without pre-pass). |
| Sky | 8-bit sRGB | Dome layers with legacy blends, decoded into the scene. |
| Sun shadow atlas | R32F + D24, `shadowMapSize` | 2x2 tiles from two cascades; falls back by halves to 1024 if allocation fails. |
| Lamp shadow atlas | R32F | One row per lamp, six faces of `lampShadowTileSize`. |
| TAA history | 2 x HalfVector4 | Compressed colour and view depth, ping-pong. |
| Environment | 64-px face atlas, octahedral 128 base + 5 levels atlas, 32x32 BRDF LUT, SH buffer (10 entries) | Rebuilt every 250 ms. |
| Post | bloom pyramid (6 levels from half resolution), 8-bit LDR target for FXAA/SMAA, SMAA edges/weights | |

The caller's targets and viewport are captured in `PristonSceneTargets.Begin` and restored by `Compose`, so a scene can
render into the back buffer or any 8-bit target (portraits, captures).

## Light model

All light is photometric (`Lighting/PristonPhotometry.cs`, `PristonDayNightCycle.cs`):

- **Sun:** rises 05:30, sets 18:45 (`Sunrise`, `Sunset`), arc east (+X) to west, tilted toward -Z. Direct illuminance
  120000 lux reduced by air mass (Kasten-Young) and broadband extinction, faded over the disc crossing the horizon;
  reddened per channel by spectral extinction (the low sun turns orange).
- **Sky:** 1200 lux at the horizon plus 20000 x sin(elevation)^0.8, falling by e every 1.6 degrees below the horizon;
  cooler than the gradient's ambient when the sun is high.
- **Night:** the day/night gradient (`DayNightSettings.DefaultGradient`) gives legacy light levels; a level becomes
  illuminance as level^1.4 x 10 lux (`LegacyLevelToIlluminance`), at half scale for moon and night sky. The moon
  shines from a fixed high direction with the colour of sunlight; the night's blue is applied by mesopic vision in the
  final pass ([shaders.md](shaders.md)), so specular light keeps the colour of the diffuse light.
- **Ground bounce:** 0.2 albedo of the horizontal illuminance for downward surfaces.
- **Stage lamps:** authored colour as a legacy level x 2, inverse-square from a shared 150-unit reference, windowed to
  zero at the authored range (600 when unset), scaled by the field's lamp intensity and range and by the lamp factor
  (off above 4 degrees of sun elevation, full at 2 degrees below). Lamps flagged as lens lights get coronas only
  unless also dynamic.
- **Transient lights** (`PristonTransientLights`, combat and spells): at most 80 alive, the 8 nearest the focus shaded,
  power decaying 70 times a second; by day scaled to 12 % of the daylight so they still read.
- **Shader units:** light colour = illuminance / pi x pre-exposure. Pre-exposure = pi x 0.4 / 2^EV100 of the incident
  reading, so a white surface under the metered light is near 1 at every hour.
- **Exposure** (`PristonFrameExposure`, `Post/PristonAutoExposure`): incident EV100 = log2(E x 100 / 250) of the
  horizontal illuminance; display scale adds `CompensationStops` (+0.5 stop day key, a dimming of up to 1.9 stops below
  EV 15 for dusk and night) and the user's compensation. With `autoExposure` the GPU meters a reflected reading
  (K = 12.5) from a 128-bin histogram, blends it with the incident reading by day (weight 0.6, night: image only) and
  adapts in 0.6 s toward brighter, 2.2 s toward darker; the light model's own change applies at once.
- **Self-illumination and display-referred art** (sky, glows, particles, VFX) divide by the display scale; at night
  emissive albedo 1 is 40 cd/m^2 (`EmissiveLuminance`).
- **Authored per-vertex light:** stage vertex colours carry the art's shading and tint the albedo in linear light.
  Scenery models (windmills, cranes, flags, static or animated) have no vertex light, so each surface takes the mean
  linear luminance of the stage's vertex light on faces with the same texture, or the stage mean
  (`PristonStageBakedLight`). Actors take the light of the highest upward floor triangle under their feet, averaged over
  a few points (`PristonStageLightField`, `RenderBatch.FloorLight`, shader `DrawLight`); `ActorFloorLight = false`
  (character select) and portraits use full light. The authored light also occludes the sky's reflections.
- **Distance fog** (`PristonDistanceFog`): from 2208 units, opacity 0.85 x (1 - exp(-0.0003 d)), colour from the sky
  capture plus forward-scattered sun (Henyey-Greenstein g = 0.7, Schlick form); the sun term fades out by 7 degrees
  below the horizon.
  `Classic` draws as `Atmospheric`.
- **Foliage:** every cut-out outside the blended queue, including late-flagged leaves, is drawn opaque by the pre-pass
  with its own depth, motion and occlusion; the lit pass shades it at that depth. Two-sided lit cut-outs transmit 45 %
  of the sun from behind.

## Shadows

- **Sun cascades** (`Lighting/PristonShadowCascades.cs`): up to 4 over 9000 units of view depth from 60, practical
  split (lambda 0.8), each slice enclosed in a sphere and snapped to whole texels (no shimmer), casters up to 4000 units
  beyond toward the light. Cascades 0-1 refresh every update, 2 every second, 3 every fourth; a new light direction or a
  focus jump over 350 units refreshes all. Refresh rate `shadowUpdateRate` (`FixedRateRefreshScheduler`), halved by
  moonlight. PCSS by day (light radius tangent 0.01), PCF (1.5 texels) for the moon. Casters draw two-sided; the
  wind/water displacement matches the lit pass.
- **Lamp shadows** (`Lighting/PristonLampShadows.cs`): the `lampShadowCount` lamps nearest the focus (after the
  transient lights) keep a row of six 90-degree cube faces storing distance / radius. A lamp keeps its row while it
  stays near; new rows draw at once, the others one every other frame. Only skinned batches (actors, animated props)
  cast; the stage's own occlusion is in its authored light.
- `Ctrl+F5` toggles both in game; `PRISTON_SHADOW_DIAG=1` enables shadow diagnostics logging.

## Screen-space and image-based passes

- **GTAO and contact shadows** (`Post/PristonScreenOcclusion.cs`): radius 90 units, at most 48 half-res pixels; contact
  march 40 units toward the sun with 12-unit thickness, sun only. The lit pass upsamples by depth; AO also gives
  specular occlusion.
- **Image-based lighting** (`Lighting/PristonEnvironmentLighting.cs`): dome faces at 64 px, SH irradiance scaled to the
  light model's sky light (dome at most at its visible level, a uniform fill for the rest), five GGX levels from a
  128-px octahedral base (64 samples), split-sum table with multiple-scattering compensation. Off:
  hemispherical sky/ground ambient.
- **SSR** (`Post/PristonScreenReflections.cs`): rays up to 3000 units, after the opaque queues; a miss keeps the IBL.
- **Volumetric light** (`Lighting/PristonVolumetricLight.cs`): extinction 4e-5 per unit with a 600-unit scale height
  around 50 units below the focus, anisotropy 0.6, march to the fog start (at most 6000), history weight 0.125.

## Anti-aliasing

- **TAA** (`Post/PristonTemporalAntiAliasing.cs`): Halton (2, 3) jitter of 8 samples; current weight 0.08 at rest to
  0.25 at 8 px/frame; variance clip gamma 1.25; depth tolerance 0.06; motion of skinned actors from the previous bone
  palette (`PristonSkinPalette` keeps the previous pose, stamped per motion frame), of wind and water from the previous
  animation time. Volumetric and AO noise rotate per frame only with TAA.
- **MSAA:** scene target multisampled (2-4), the back buffer never.
- **FXAA / SMAA 1x:** on the 8-bit display image after the final pass.

## Diagnostics

- `LastFrameStats` (`PristonRenderStats`): batches total/visible/culled, draw calls, triangles, state and texture
  changes, dynamic upload bytes, sun shadow update, lamp shadow cube maps redrawn (`L` in the title), shadow draw
  calls. Shown in the window title (`Ctrl+F3`).
- `Profiler` (`PristonFrameProfiler`): CPU time per `RenderSection`; GPU time from Vulkan timestamps after
  `EnableGpuTimers` (benchmark only), read back 4 frames later without waiting.
- `LastStreamingStats`: per `StreamingStep` count, total and worst main-thread milliseconds and bytes of the last stage
  upload (`[Stream] upload steps` log line).
- `--benchmark-render <assets> <content> <report.json> <fieldId> <settings.json> [seconds runs hour orbit [distance
  [pitch [focusX focusZ [yaw]]]]]` in the regression runner writes frame/update/draw/present times, `CpuSectionsMs`,
  `GpuSectionsMs`, `StreamingUploadSteps` and a `<report>.png` capture ([testing.md](testing.md)).
- `--verify-shaders`, `--capture-sky`, `--check-look` and the render test suites (`RenderTests`, `RenderFidelityTests`,
  `PhotorealTests`, `MaterialAdapterTests`, `ShadowProjectionTests`, `SkinPaletteTests`, `ModelNormalTests`,
  `TransientLightTests`, `SkyTests`, `CameraTests`) cover the renderer.

## How to

- **Add a scene pass:** shader and owner per [shaders.md](shaders.md#how-to); add a `RenderSection` value; call it at
  the right point of `PristonRenderer.Draw` (or `PristonSceneTargets.Compose` for post), rebind the scene with
  `_sceneTargets.Bind()` and `_stateCache.InvalidateDeviceState()` after any off-screen target; gate it on the settings
  it needs (pre-pass, sun) and dispose it in `Dispose`; add a setting if optional
  ([client-settings.md](client-settings.md)); update the frame-graph table here and the benchmark section names in
  [testing.md](testing.md).
- **Draw something new into the HDR scene from another project:** draw between `Draw` and `ComposeScene`, use
  `PristonUnlitEffect` or `PristonVfxEffect` (display-referred colour divided by `SceneExposure`) and read
  `SceneLinearDepth` only when non-null.
- **Change a lighting constant:** the CPU side lives in `PristonPhotometry`, `PristonDayNightCycle` or the constants at
  the top of `PristonRenderer`; mirrored shader values are listed in [shaders.md](shaders.md#pitfalls). Re-run
  `--verify-shaders`, the render tests and `--benchmark-render` captures by day and at 21:00.
- **Support a new legacy asset variant:** extend the reader in `Formats/`, keep unknown bytes in metadata, resolve paths
  through `PristonFileResolver`, add a fixture test (`BinaryCursorTests`, `RenderTests`), and describe the format here
  without citing the original source.
- **Add a streaming step:** yield a `StreamingWork` with a `StreamingStep` from `StageUploadSteps`; keep each step one
  indivisible GPU call; do CPU work in `Task.Run` and yield `StreamingWork.Wait` while polling.

## Pitfalls

- `Draw` leaves the HDR scene bound; anything drawn before `ComposeScene` lands in it, and the UI must come after.
- Binding a render target ends the Vulkan render pass; the profiler resets timestamps only outside one, so
  `_sceneTargets.Begin` precedes `Profiler.BeginFrame`.
- The sky capture is refreshed before the scene target is cleared, because binding another target may discard it.
- MonoGame clears `DiscardContents` targets on bind; scene, shadow and LDR targets are `PreserveContents` on purpose.
- Skinned meshes have the opposite winding of the stage and are always drawn two-sided.
- Cut-outs must stay in the alpha-tested queue (pre-pass depth, motion, AO); moving foliage to the blended queue makes
  it take the depth, motion and fog of what is behind (ghosted canopies under TAA).
- `_sunElevation` holds the sine of the elevation; thresholds such as `> -0.05` are in sine, not radians.
- Light selection is relative to the camera focus (`camera.Target`), not the camera position.
- Transient lights fill the first local-light slots; with `localLightLimit` 8 a fight can leave no stage lamps.
- Texture cache keys include the full directory: the same file name in two directories is two textures.
- Some comments in `Formats/` name functions of the original client; do not copy those names into new code or docs.

## Related

- [shaders.md](shaders.md): passes, conventions, register maps, shader validation.
- [client-settings.md](client-settings.md): every option, presets and GPU timings.
- [effects.md](effects.md): particles and VFX drawn into the HDR scene.
- [client.md](client.md): screens, `GameScene`, field streaming, HUD portrait.
- [world.md](world.md): server-side stage collision and the shared geometry.
- [testing.md](testing.md): regression runner, probes, benchmark arguments.
- [architecture.md](architecture.md): projects and threading model.
