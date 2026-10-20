# Content/Shaders: the SM 6.0 shader library

The client's shaders, compiled by MGCB for MonoGame's Native platform on Vulkan (`/platform:DesktopVK`) with the
effect processor of the MonoGame fork (`monogame/pipeline`, `SpirvEffectProcessor`: HLSL to SPIR-V through DXC,
`vs_6_0`/`ps_6_0`). Content project: `../Content.mgcb`. The library replaced the SM 3 shaders of the DesktopGL build;
its structure follows WingSim's library (`F:\WingSim\src\WingSim.Shaders`).

## The renderer they serve

An HDR forward renderer (`Client/PristonTale.Engine3D/Rendering`):
- the scene is drawn into a HalfVector4 target with depth (`PristonSceneTargets`) in pre-exposed linear radiance: every
  light is a photometric quantity (`Lighting/PristonPhotometry.cs`: sun, sky and moon illuminance of the hour from
  `PristonDayNightCycle`, the stage lamps and night levels from their authored light levels) times the frame's
  pre-exposure, pi over the incident illuminance, so a white surface under the metered light is near 1 at every hour;
- one material model for every surface (metallic-roughness, `Materials/`), fed by `PristonMaterialAdapter`, which turns
  each legacy material into its inputs at load time;
- the sun (or the moon) with stabilized cascaded shadows in one R32F atlas (`Lighting/PristonShadowCascades.cs`,
  PCF or PCSS), up to 32 local lights culled per 16x16-pixel tile by `Passes/LightCull.fx` (stage lamps, of which the
  nearest four keep cube shadows that actors cast, `Lighting/PristonLampShadows.cs`; and combat lights), a sky/ground
  ambient and the distance fog toward the captured sky (`Lighting/`, `Atmosphere/`);
- a depth pre-pass (`Surface.fx` `Prepass`) writes the opaque surfaces' depth, linear depth, normals and motion; the lit
  pass then shades each pixel once, and `Passes/AmbientOcclusion.fx` computes GTAO and contact shadows at half
  resolution from it, which the lit pass upsamples by depth (ambient and specular occlusion, sun visibility);
- image-based lighting from the sky dome (`Passes/Environment.fx`, `Lighting/PristonEnvironmentLighting.cs`): its six
  cube faces to an octahedral HDR map, five GGX-prefiltered levels in one atlas, order-2 spherical harmonics of the
  irradiance (compute) scaled to the light model's sky light, and a split-sum BRDF table with multiple-scattering
  energy compensation; the ambient occlusion and specular occlusion apply to it;
- after the opaque passes, `Passes/Reflections.fx` adds screen-space reflections of water and ice (Ultra) over the
  image-based reflection; after the scene, `Passes/Volumetric.fx` adds the sunlight a height fog scatters, shadowed by
  the cascades, marched at quarter resolution;
- the vertex colour scales the albedo in linear light on both paths: the stage's authored light on stage geometry and,
  per texture, on the stage's scenery models (`PristonStageBakedLight`), and on actors the light of the floor under them
  (`PristonStageLightField`, the skinned `DrawLight` constant); that light also occludes the sky's reflections; the
  legacy sphere-mapped shine reflects the sky and a quarter of the sun, occluded and shadowed, and counts as specular
  light; the sky's order-2 irradiance is windowed (Lanczos sigma) against ringing at the horizon, the specular atlas's
  scale taken from the unwindowed sky;
- thin two-sided cut-outs (foliage) transmit the sun lit from behind (`Translucency`, `PristonMaterialAdapter`); every
  cut-out outside the blended queue, the leaves flagged to draw late included, is drawn by the depth pre-pass and shaded
  opaque at that depth; the stage lamps fall off with the inverse square of the distance from a shared 150-unit
  reference, windowed to zero at their range;
- display-referred inputs (the sky dome, self-illumination by day, particles and glows) are divided by the frame's
  nominal display scale (`PristonFrameContext.Exposure`), so they display at their authored level when the automatic
  exposure sits at the light model's reading; self-illumination becomes a light source of fixed luminance at night;
- the sky dome is composed display-referred: its layers are art authored for sRGB blending (additive glows, alpha
  clouds), so they are blended into an 8-bit target with their legacy blend modes and decoded by
  `Passes/SkyComposite.fx`, the pass that starts every scene target;
- with temporal anti-aliasing the opaque passes (or the pre-pass) also write a motion target (uv motion, previous and
  current view depth, negative on cut-outs, which the resolve treats as reactive); the projection is jittered;
- `ComposeScene` runs the post chain: `PostTemporal` (resolve), `PostExposure` (compute histogram and adaptation),
  `PostBloom` (pyramid), `PostFinal` (sharpening, exposure, PBR Neutral, AgX or ACES, sRGB, 3D LUT grade, dither; FXAA)
  and `PostSmaa`; the UI is drawn after it in sRGB.

Pass order and profiler sections: `PristonRenderer.Draw` and `PristonRenderer.ComposeScene`.

## Layout

| Path | Contents |
|---|---|
| `Core/` | `Common.hlsli` (constants, `SafeNormalize`), `Color.hlsli` (sRGB curves, PBR Neutral, `DisplayEncode`), `Frame.hlsli` (`FRAME_CONSTANTS`), `Fullscreen.hlsli` (the full-screen triangle). |
| `Lighting/` | `Octahedral.hlsli` (direction mapping), `Brdf.hlsli` (GGX, height-correlated Smith visibility, Schlick, specular anti-aliasing), `LightingDecl.hlsli` (`LIGHTING_CONSTANTS`, `LIGHTING_RESOURCES`), `Shading.hlsli` (cascaded sun shadows with PCF/PCSS, lamp cube shadows, screen-space occlusion, tiled local lights, ambient with specular occlusion). |
| `Atmosphere/` | `FogDecl.hlsli`, `Fog.hlsli`: the distance fog and the sky capture lookup. |
| `Materials/` | `MaterialDecl.hlsli` (`MATERIAL_CONSTANTS`, `MATERIAL_RESOURCES`), `Material.hlsli` (`SampleSurface`: base colour, the second layer, alpha test). |
| `Vertex/` | `SkinningDecl.hlsli` + `Skinning.hlsli` (rigid one-bone skinning, current and previous pose), `VertexAnimationDecl.hlsli` + `VertexAnimation.hlsli` (water ripple, wind, at any time). |
| `Passes/` | `Surface.fx` (built as `Effects/StaticSurface` and, with `SKINNED`, `Effects/SkinnedSurface`; techniques `Prepass`, `PrepassMasked`, `Opaque`, `Masked`, `Sky`, `ShadowCaster`, `ShadowCasterMasked`, `ShadowClear`), `Unlit.fx`, `AmbientOcclusion.fx` (`Occlusion`, `Blur`), `LightCull.fx` (compute), `Environment.fx` (`Octahedral`, `Prefilter`, compute `Irradiance`), `Reflections.fx` (`Copy`, `Reflect`), `Volumetric.fx` (`March`, `Composite`), `SkyComposite.fx` (`SkyComposite`, `Background`), `PostTemporal.fx`, `PostExposure.fx` (compute: `Histogram`, `Adapt`), `PostBloom.fx`, `PostFinal.fx` (`Tonemap`, `Fxaa`), `PostSmaa.fx` (SMAA 1x, MIT; `Rendering/Post/Smaa/CREDITS.md`). |

## Conventions

- **One cbuffer per stage** (MonoGame's Vulkan effects): frame block first, then the pass's and the draw's values. A
  name is declared once per effect; a value both stages need is passed from the vertex stage as a varying. MonoGame
  uploads a stage's whole cbuffer when any of its parameters changes, so the C# side skips unchanged values.
- **Shared declarations come in pairs:** `XDecl.hlsli` defines macros expanded inside the cbuffer and at file scope with
  the effect's registers; `X.hlsli`, included after the cbuffer, holds the functions.
- **Every texture and sampler has an explicit register**, the sampler on its texture's index, at most 16 per stage. Every
  slot is bound before its first draw (`PristonShaderLibrary` binds a white 1x1 texture), so no draw samples an unbound
  slot. No uniform initialisers (the compiler drops them).
- **Vertex inputs:** each shader's input struct lists what it reads; the fork's runtime matches inputs by semantic.
  `Color` elements arrive as normalised floats; legacy vertex colours and textures are sRGB and are decoded exactly.
- **Units:** pre-exposed linear Rec.709 radiance. Light colours are the radiance of a white Lambertian surface facing
  the light (illuminance / pi, times the pre-exposure), so a lit diffuse surface is albedo x colour x n.l and the
  specular lobe carries the matching pi.
- **Compute and storage resources:** a compute pass holds only its compute shader, with `[numthreads]`; read-only
  `StructuredBuffer` take t registers in any stage, `RW*` resources u registers in compute shaders only
  (`monogame/issues/005` to `008`).
- **Vulkan conventions the code relies on** (the processor compiles with D3D conventions: `-fvk-invert-y`,
  `-fvk-use-dx-layout`, `-fvk-use-dx-position-w`): clip space is D3D's, +Y up and depth 0 to 1 (XNA's projection
  matrices produce it directly); render-target and texture coordinates start at the top-left, so a projected position
  maps to uv = (x, -y) * 0.5 + 0.5 (the sun shadow lookup, the full-screen triangle); matrices are XNA row-vector
  matrices used as `mul(v, M)`. MonoGame clears a `DiscardContents` render target whenever it is bound, so targets that
  are rebound inside a frame (the scene) use `PreserveContents` and are cleared explicitly.

## Validation

`--verify-shaders` in the regression runner (`tests/PristonTale.Tests/README.md`) draws every technique on the GPU and
checks the material model against the same formulas in C#, the motion target, the exposure histogram and adaptation,
the bloom pyramid, the display transforms and the colour grade against their CPU references
(`Rendering/Post/PristonDisplayTransform.cs`, `PristonColorGrading.cs`) and the temporal resolve.
