# Effects and VFX

Particle and skill-effect runtime of the client: `Client/PristonTale.Effects/` (native VFX system, legacy particle
engine, legacy particle script reader) and the VFX data in `Client/PristonTale.Gameplay/Effects/Vfx/`, plus the
gameplay glue in `Client/PristonTale.Gameplay/Effects/SkillPresentation.cs` that turns skill events into effects.

## Read this when

- Adding or tuning the visual effect of a skill (cast, projectile, impact, aura, weapon trail).
- Overriding an effect locally with a file under `assets/Vfx/` without rebuilding.
- Adding a field to the `.vfx.json` format, a builtin texture, or a new emitter behaviour.
- Debugging an effect that is invisible, a white square, stuck alive, at the wrong position or on the wrong bone.
- Working on potion, meteor, character-select or opening effects that use the legacy `EffectsEngine`.
- Reading or converting an original particle script (`.part`).

## Code map

| Path | Responsibility |
|---|---|
| [`Client/PristonTale.Effects/Vfx/VfxDefinition.cs`](../Client/PristonTale.Effects/Vfx/VfxDefinition.cs) | Authoring schema, `VfxDefinition.Json` options, `ParseFile`, `Validate` |
| `Client/PristonTale.Effects/Vfx/VfxLibrary.cs` | Name -> `CompiledVfx` registry; texture resolution; `LoadDirectory` for overrides |
| `Client/PristonTale.Effects/Vfx/VfxCompiled.cs` | `VfxCurve` (keyed curve sampler), `CompiledEmitter`, `CompiledTrail`, `CompiledVfx` |
| [`Client/PristonTale.Effects/Vfx/VfxSystem.cs`](../Client/PristonTale.Effects/Vfx/VfxSystem.cs) | `VfxSystem`, `VfxInstance`, emitter/trail simulation, `IVfxAnchors`, `IVfxServices` |
| `Client/PristonTale.Effects/Vfx/VfxRenderer.cs` | Builds quads for all instances, one dynamic vertex buffer, draws runs by (blend, texture) |
| `Client/PristonTale.Effects/Vfx/VfxBuiltinTextures.cs` | Procedural `builtin:` sprite textures |
| `Client/PristonTale.Effects/Vfx/LegacyVfxImporter.cs` | Converts parsed legacy particle sequences into a `VfxDefinition` |
| `Client/PristonTale.Effects/Legacy/` | `.part` script parser (`LegacyParticleScript`) and per-particle evaluation (`LegacyParticleSequence`) |
| `Client/PristonTale.Effects/EffectsEngine.cs`, `Effects/` | Legacy effect engine: `EffectCatalog`, `EffectDefinition`, `EffectInstance` |
| `Client/PristonTale.Effects/Particles/` | Legacy emitter model: `ParticleEmitter`, `Particle`, ranges, over-life curves, `ParticleAnimation`, `PotionParticleMotion` |
| `Client/PristonTale.Effects/Rendering/` | Legacy `ParticleRenderer`, `ParticleBlendMode` + `ParticleBlendStates`, `ParticleVertex` |
| `Client/PristonTale.Gameplay/Effects/Vfx/Common.vfx.json` | Shared effects (generic trails, casts, impacts, buffs, projectiles) |
| `Client/PristonTale.Gameplay/Effects/Vfx/Classes/<Class>.vfx.json` | Per-class effects (Archer, Atalanta, Fighter, Knight, Magician, Mechanician, Pikeman, Priestess) |
| [`Client/PristonTale.Gameplay/Effects/SkillPresentation.cs`](../Client/PristonTale.Gameplay/Effects/SkillPresentation.cs) | Loads the library, implements `IVfxServices`, maps events to effects |
| `Client/PristonTale.Gameplay/Effects/LegacyEffectLibrary.cs` | Loads original textures, particle scripts (cached), potion/hit/meteor legacy effects |
| `Client/PristonTale.Engine3D/Rendering/PristonVfxEffect.cs` | Wrapper of the VFX shader (`Effects/Vfx`, source `Client/PristonTale/Content/Shaders/Passes/Vfx.fx`) |
| `tests/PristonTale.Tests/SkillPresentationTests.cs` | Regression cases for definitions, assets, system, importer |

## Two engines

| | Native VFX (`Vfx/`) | Legacy (`EffectsEngine`) |
|---|---|---|
| Data | `.vfx.json` definitions | Original `.part` scripts and hard-coded legacy definitions |
| Used by | Skill casts, projectiles, impacts, auras, revive | Potions, hulk hits, meteors, monster mesh effects, character select, opening cinematic, legacy layers of VFX |
| Shader | `PristonVfxEffect` (soft particles) | `PristonUnlitEffect` (no soft fade) |
| Blends | Additive, Alpha | Additive, AlphaBlend, InverseShadow, ColorBlend |
| Budget | `VfxSystem.MaxInstances` = 256 live instances | None inside the engine; callers stop spawning at 128 instances |

A VFX `legacy` layer bridges the two: `VfxSystem` asks `IVfxServices.SpawnLegacy`, and `SkillPresentation` spawns the
script into the scene's `EffectsEngine`.

## VFX file format

A `.vfx.json` file is a JSON array of effect objects. `VfxDefinition.Json` controls parsing: camelCase property names
(matching is case-sensitive), `//` and `/* */` comments allowed, trailing commas allowed, enums as strings via
`JsonStringEnumConverter` (written PascalCase). Unknown properties are ignored. `name` is `required`.

Units: positions, sizes, speeds in world units (Y up); times in seconds; angles in degrees; colours linear Rec.709
floats. `Offset`, emitter shape offsets and directions are rotated by the spawn facing (`VfxInstance.Orientation`, a
Y rotation); the shipped files treat effect-local -Z as the caster's forward. Ranges are `[min, max]` with
`max >= min`; each particle picks a uniform value inside.

### Effect (`VfxDefinition`)

| JSON | Type | Default | Meaning |
|---|---|---|---|
| `name` | string | required | Unique key (case-insensitive lookup). Convention `vfx.<class>.<skill>.<role>` or `vfx.<role>.<variant>` |
| `duration` | float s | 1 | Emission window, `0 < duration <= 60`. Layers stop emitting after it; the instance ends when every layer is dead |
| `emitters` | emitter[] | [] | Sprite particle emitters |
| `trails` | trail[] | [] | Ribbon trails following an anchor |
| `lights` | light[] | [] | One-shot transient point lights |
| `sounds` | sound[] | [] | One-shot sounds |
| `legacy` | legacy[] | [] | Original particle scripts played unchanged |

### Emitter (`VfxEmitterDefinition`)

| JSON | Type | Default | Meaning |
|---|---|---|---|
| `texture` | string | "" | Asset path under the asset root (`Effect/Particle/x.tga`) or `builtin:<name>`; empty -> white |
| `frameColumns`, `frameRows` | int >= 1 | 1, 1 | Sprite sheet grid; frame = floor(life x frames), row-major from top-left |
| `blend` | `Additive` \| `Alpha` | Additive | Additive = src-alpha + dest; Alpha = non-premultiplied over |
| `intensity` | float | 1 | Multiplies RGB (not alpha); 1 = authored colour at the current exposure, higher blooms |
| `softness` | float units | 16 | Soft-particle fade distance against scene depth; 0 disables (use for ground-aligned decals) |
| `anchor` | `Point` \| `Caster` \| `Target` | Point | Spawn point (impact or ground target), caster entity or target entity |
| `bone` | string | "" | Bone of the anchor actor (`Bip weapon01`, `Bip01 R Hand`); ignored for `Point` or when not found |
| `offset` | float[3] | [0,0,0] | From the anchor (or bone), rotated by facing |
| `local` | bool | false | Live particles move with the anchor instead of staying where born |
| `delay` | float s | 0 | Start of this emitter after spawn |
| `emitTime` | float s | 0 | Length of continuous emission at `rate`; 0 = until `duration` |
| `rate` | float /s | 0 | Continuous emission, >= 0 |
| `burst` | int | 0 | Particles emitted once on the first active frame, >= 0 |
| `maxParticles` | int | 64 | Pool size, 1..4096; emission beyond it is dropped |
| `lifetime` | range s | [1,1] | Per particle; min > 0 |
| `speed` | range units/s | [0,0] | Launch speed |
| `direction` | float[3] | [0,1,0] | Cone axis (normalized; all zeros = up) |
| `spread` | float deg | 0 | Cone half-angle, clamped 0..180 (180 = every direction) |
| `shape` | `Point` \| `Sphere` \| `Ring` \| `Disc` | Point | Spawn shape: sphere surface, XZ circle, or XZ filled disc |
| `radius` | range units | [0,0] | Shape radius, sampled per particle |
| `gravity` | float[3] units/s^2 | [0,0,0] | Constant acceleration |
| `drag` | float /s | 0 | Fraction of velocity lost per second (`v *= max(0, 1 - drag*dt)`) |
| `size` | key[] width 1 | `[{t:0,v:[10]}]` | Half-extent of the quad over normalized life |
| `color` | key[] width 4 | `[{t:0,v:[1,1,1,1]}]` | Linear RGBA over normalized life |
| `rotation` | range deg | [0,0] | Initial sprite rotation (ignored by `Velocity` alignment) |
| `spin` | range deg/s | [0,0] | Rotation speed |
| `align` | `Camera` \| `Ground` \| `Velocity` | Camera | Billboard, flat on XZ (world axes, not facing), or stretched along velocity |
| `stretch` | float | 0.02 | `Velocity` alignment: quad length x (1 + speed x stretch) |

A key is `{ "t": <0..1>, "v": [..] }`; `t` must be non-decreasing; sampling is linear between keys and clamped at the
ends (`VfxCurve`).

### Trail (`VfxTrailDefinition`)

| JSON | Type | Default | Meaning |
|---|---|---|---|
| `texture`, `blend`, `intensity` | as emitter | "", Additive, 1 | U runs head (0) to tail (1), V across the width |
| `anchor`, `bone`, `offset` | as emitter | **Caster**, "", [0,0,0] | Point the ribbon follows (usually `Bip weapon01`) |
| `width` | float units | 12 | Full ribbon width, > 0 |
| `lifetime` | float s | 0.25 | Life of each trail point, > 0; points are sampled every `lifetime / 32` s |
| `delay`, `emitTime` | float s | 0, 0 | As emitter |
| `color` | key[] width 4 | white -> transparent | Colour from head (t = 0) to tail (t = 1) |

Trails use a fixed soft-particle distance of 8 units and keep at most 32 points (`VfxSystem.TrailPoints`).

### Light (`VfxLightDefinition`)

| JSON | Type | Default | Meaning |
|---|---|---|---|
| `anchor`, `offset` | as emitter | Point, [0,30,0] | No `bone`; position fixed when fired |
| `color` | float[3] | [1,1,1] | Converted to an 8-bit `Color`: channels clamp to 0..1 |
| `power` | int > 0 | 160 | Legacy light units: radius `max(64, power/2)`, brightness `power x colour / 256` |
| `decay` | int > 0 | 4 | Power lost per 1/70 s tick; lifetime `ceil(power/decay)/70` s |
| `delay` | float s | 0 | Fire time |

Lights go to `PristonTransientLights.Add` through `IVfxServices.AddLight`; see docs/rendering.md for lighting.

### Sound (`VfxSoundDefinition`) and legacy layer (`VfxLegacyDefinition`)

| JSON | Type | Default | Meaning |
|---|---|---|---|
| sound `path` | string | "" | Asset-relative wave path (`wav/Effects/...`), played at the caster anchor |
| sound `delay`, `volume` | float | 0, 1 | Fire time; volume scale |
| legacy `script` | string | "" | Script name; file `<assetRoot>/Effect/Particle/Script/<script>.part` |
| legacy `anchor`, `bone`, `offset`, `delay` | as emitter | Point, "", [0,0,0], 0 | Spawn position and time |
| legacy `follow` | bool | false | Move the script's origin with the anchor every frame |

`Validate` checks effect, emitter, trail and light fields; sound and legacy layers are not validated. Shipped data uses
no `sounds` layers: skill sounds come from the skill rules (`castSound`, `impactSound`, `companionSound`).

### Example (from `Classes/Knight.vfx.json`)

```json
{
  "name": "vfx.knight.piercing.thrust", "duration": 0.9,
  "legacy": [{ "script": "Piercing1", "anchor": "Caster", "offset": [0, 30, -20], "delay": 0.35 }],
  "trails": [{ "texture": "builtin:beam", "anchor": "Caster", "bone": "Bip weapon01", "width": 12, "lifetime": 0.2,
    "intensity": 3, "color": [{ "t": 0, "v": [1, 0.95, 0.75, 0.9] }, { "t": 1, "v": [1, 0.6, 0.2, 0] }] }],
  "emitters": [
    { "texture": "builtin:spark", "anchor": "Caster", "offset": [0, 30, -30], "delay": 0.38, "burst": 30,
      "maxParticles": 30, "lifetime": [0.2, 0.35], "speed": [260, 420], "direction": [0, 0, -1], "spread": 8,
      "drag": 1.5, "align": "Velocity", "stretch": 0.05, "intensity": 5,
      "size": [{ "t": 0, "v": [3] }, { "t": 1, "v": [0.5] }],
      "color": [{ "t": 0, "v": [1, 0.95, 0.8, 1] }, { "t": 1, "v": [1, 0.55, 0.2, 0] }] }
  ],
  "lights": [{ "anchor": "Caster", "offset": [0, 30, -40], "color": [1, 0.85, 0.6], "power": 160, "decay": 7,
    "delay": 0.38 }]
}
```

## Loading and override semantics

`SkillPresentation.LoadLibrary` runs when `GameScene` creates its renderer (`EnsureRenderer` ->
`CreateSkillPresentation`), on the game thread:

1. `EmbeddedDefinitions()` reads the manifest resources `PristonTale.Gameplay.Effects.Vfx.*.vfx.json`
   (`PristonTale.Gameplay.csproj` embeds `Common.vfx.json` and `Classes/*.vfx.json`); `Common` first, then class files
   in ordinal order. Missing resources throw.
2. `VfxLibrary.LoadDirectory(<assetRoot>/Vfx)` loads `*.vfx.json` from that directory only (not recursive), in ordinal
   file-name order. A missing directory loads nothing.
3. `Register` keys by name (case-insensitive); a later registration replaces the whole definition. There is no
   field-level merge. Overrides therefore win over shipped effects, and among overrides the last file wins.

`<assetRoot>` is the client's asset directory (`assets/` by default, see docs/client.md). An embedded file that fails
to parse or validate throws out of the `SkillPresentation` constructor (a build defect); an `assets/Vfx` file that
cannot be read, parsed or validated is skipped as a whole and logged as `[Effects] skipped effect file ...`.

Compilation (`VfxLibrary.Register`) validates, resolves textures and precomputes per layer: `CompiledEmitter` (offset,
normalized direction, gravity, `VfxCurve` size and colour, cosine of the spread) and `CompiledTrail`. `CompiledVfx` is
shared by every instance. Texture resolution: empty path -> fallback (white); `builtin:<name>` -> `VfxBuiltinTextures`
(validation rejects a name outside `VfxBuiltinTextures.Names`); otherwise `LegacyEffectLibrary.LoadTexture` (null when
the file is missing) -> fallback. A missing texture therefore draws a tinted white quad instead of hiding the effect.

## Runtime: `VfxSystem`

- `Spawn(CompiledVfx, VfxSpawn)` returns a pooled `VfxInstance`, or null when 256 instances are live.
  `VfxSpawn(CasterId, TargetId, Point, Facing)`: entity ids are server serials (0 = none), `Point` is client world
  space, `Facing` radians for the Y rotation.
- `Update(seconds)` ignores non-positive or non-finite steps and clamps to 0.1 s. Per instance: advance age, move a
  travelling spawn point, refresh caster/target positions through `IVfxAnchors.TryGetPosition` (both start at
  `Point`), update emitters and trails, fire one-shots (lights, sounds, legacy layers) once their delay is reached,
  follow or stop legacy instances. `emitting = !stopped && age <= duration`; an instance is alive while emitting or
  while any particle, trail point or legacy instance lives. Dead instances return to the pool.
- `VfxInstance.Stop()` ends emission; unfired lights, sounds and legacy layers never fire; legacy instances stop.
- `VfxInstance.Travel(from, to, seconds, arc)` moves `Point` linearly with a parabolic lift peaking at `arc` units
  mid-flight, then stops the instance on arrival (projectiles, chain links, companion strikes).
- Anchor resolution (`AnchorPosition`): `Point` -> spawn point; `Caster`/`Target` -> last known entity position;
  with a `bone` and a non-zero entity id, `IVfxAnchors.TryGetBone` replaces it when it succeeds. The offset is then
  added after rotation by facing. `GameScene` implements bones through `PristonRenderer.TryGetActorBoneWorld`.
- Emitters: burst on the first active frame, then `rate` through a fractional accumulator during
  `[delay, delay + emitTime]`. Integration per frame: `v = (v + gravity*dt) * drag; p += v*dt`. With `spread`
  below about 0.8 degrees, `Ring`, `Disc` and `Sphere` shapes emit radially outward from the centre and ignore
  `direction`.
- Randomness: per-instance xorshift seeded by an LCG in the system; repeatable only for an identical spawn sequence
  and unrelated to the server. Effects are presentation only.
- Memory: instances pooled on a stack, particle arrays rented from `ArrayPool`, trail buffers fixed; after warm-up an
  update allocates nothing (asserted by the regression case).

Threading: `SkillPresentation.Update` (calls `VfxSystem.Update`) and `SkillPresentation.Draw` both run on the
MonoGame game thread from `GameScene`; the system has no locks and must not be touched from other threads.

## Drawing: `VfxRenderer`

`GameScene.Draw` order: `PristonRenderer.Draw` (opaque scene, depth pre-pass) -> monster mesh effects -> return
effect -> legacy `EffectsEngine.Draw` -> `DrawSkillEffects` (VFX) -> `PristonRenderer.ComposeScene` (post). VFX
therefore draw into the bound HDR scene target before bloom and tone mapping.

- Quads for all instances go into one `VfxVertex` array (position, linear RGBA x intensity, UV, `Params.x` =
  softness), uploaded once to a `DynamicVertexBuffer` with a shared 32-bit quad index buffer; at most 16384 quads
  per frame, the rest are dropped.
- Pass 1 draws `Alpha` layers of all instances, instances sorted back to front by caster position distance to the
  camera (particles inside an instance are not sorted). Pass 2 draws `Additive` layers. Consecutive quads with the
  same (blend, texture) form one draw call (`LastQuadCount`, `LastDrawCalls` for diagnostics).
- State: `DepthStencilState.DepthRead` (test, no write), `CullNone`, `BlendState.Additive` or `NonPremultiplied`.
- Shader (`Vfx.fx` via `PristonVfxEffect`): texture RGB decoded from sRGB to linear, multiplied by the vertex colour,
  divided by the scene exposure (`PristonUnlitEffect.SceneExposure`, set by the renderer each frame) and capped.
  Soft particles: when `PristonRenderer.SceneLinearDepth` is non-null (the pre-pass ran), alpha is multiplied by
  `saturate((sceneDepth - particleDepth) / softness)`; a null depth disables the fade. `DisplayOutput = true` encodes
  display sRGB for 8-bit targets (probes, tools). Shader details: docs/shaders.md.

## Builtin textures

`VfxBuiltinTextures` generates 128x128 textures per `GraphicsDevice`: white RGB with the shape in alpha, so the vertex
colour sets the hue. Coordinates span -1..1 with V downwards. `SkillPresentation` owns and disposes the instance.

| Name | Shape |
|---|---|
| `builtin:glow` | Gaussian blob (sigma 0.32) |
| `builtin:soft` | Smooth-edged disc |
| `builtin:spark` | Gaussian narrow in X, long in Y (pairs with `Velocity` alignment) |
| `builtin:beam` | Gaussian that varies only along V: a bright centre line along U (ribbon core for trails) |
| `builtin:ring` | Thin ring at radius 0.78 |
| `builtin:star` | Small core plus horizontal and vertical flares |
| `builtin:smoke` | Soft disc modulated by value noise |
| `builtin:flame` | Soft disc shifted upward, modulated by value noise |

## Legacy particle scripts and `EffectsEngine`

Script files live at `<assetRoot>/Effect/Particle/Script/<name>.part`, read as Latin-1 through `PristonFileResolver`
by `LegacyEffectLibrary` (`ParticleScript` caches the compiled `EffectDefinition`, including a null for a failed
script; `ReadParticleSequences` returns raw sequences). `LegacyParticleScript.Read` accepts:

- Header `particlesystem "<name>" 1.00 {`, an optional constant `position = xyz(...)` (ignored), then 1..32
  `eventsequence "<name>" {` blocks and a closing `}`. `//` comments.
- Sequence properties: `sourceblendmode` (`BLEND_LAMP` -> Additive, `BLEND_INVSHADOW` -> InverseShadow,
  `BLEND_COLOR` -> ColorBlend), `loops` (integer 1..256), `numparticles` (1..4096, count x loops <= 65536),
  `gravity` (constant xyz), `emitrate` (> 0 per second), `emitradius` (xyz range), `lifetime` (<= 60 s),
  `texture` (must stay under `effect/particle/`), `delay` (0..60 s), `particletype` (`TYPE_ONE` billboard,
  `TYPE_TWO` ground plane).
- Up to 128 events: `initial <p>`, `at <seconds> <p>`, `final <p>` (time = max lifetime), optionally prefixed
  `fade so` to interpolate from the previous event instead of stepping. Properties: `size`, `sizeext`, `color`
  (`rgba` 0..255), `velocity` (xyz, no fade), `partanglez` (4096 units per turn; X/Y angles must be zero),
  `localanglez` (zero only). Components are numbers or `random(a,b)`.
- Anything else throws `InvalidDataException`.

`LegacyParticleSequence` evaluates particles statelessly from a per-particle seed (hashed channel sampling, no RNG
stream): size and extension halved for the billboard, colour / 255, position = origin + piecewise-constant velocity
plus gravity, with the script Z axis negated and rotated by the spawn orientation. `ParticleEmitter` in script mode
timestamps births at `delay + n / emitrate` up to count x loops, so playback is independent of frame rate and a
hitch never builds an emission backlog; its pool holds `numparticles` live particles.

`ParticleEmitter` in generic mode (hard-coded legacy definitions) supports rate, burst, cone direction, spherical
spawn radius, gravity, damping (`pow(1 - damping, dt)`), start/end colour and size, flipbook frames (by lifetime or
frames per second), `ParticleAnimation` keyframes and `PotionParticleMotion` (fixed 70 Hz integer motion with an
orbit). `EffectsEngine` derives a deterministic seed per spawn from its constructor seed and a counter;
`EffectInstance` stops its emitters after `Duration` unless `Loop` (0 = run to completion). `ParticleRenderer`
batches by (texture, blend) with `PristonUnlitEffect`; `TYPE_TWO` sequences draw flat on XZ.

## `LegacyVfxImporter`

`LegacyVfxImporter.Convert(name, sequences, intensity)` maps each sequence to one emitter: `numparticles` -> `burst`,
loops > 1 -> `rate = emitrate x count` and `emitTime = loops / emitrate`, emit radius -> `Sphere` with
`radius [0, max component]`, first velocity event -> speed range, mean direction and spread (180 when the velocity
range is wide, else 25), gravity (Z negated), size and colour events -> keys over the mean lifetime (size halved,
colour / 255, steps duplicated as hold keys), `TYPE_TWO` -> `Ground` alignment with softness 0. Duration covers the
longest sequence, capped at 60 s.

No runtime path or tool calls it; the only caller is the regression case `ImporterAsync`. To use it, read the
sequences with `LegacyEffectLibrary.ReadParticleSequences(assetRoot, name)`, convert, and serialize with
`JsonSerializer.Serialize(new[] { definition }, VfxDefinition.Json)` from a scratch program or test, then hand-edit the
result. For exact original playback use a `legacy` layer instead.

## Skill events to effects (`SkillPresentation`)

Skill rules (`Shared/PristonTale.Simulation/Skills/Data/Classes/<Class>.json`, `SkillRule`) name effects in
`castEffect`, `impactEffect`, `projectileEffect`, `buffEffect`, `companionEffect`, `modes[i].impactEffect` and
`charge.full.impactEffect`; `projectileArc` feeds `Travel`. Events of other entities run 150 ms late
(`RemoteDelayMs`) to match interpolated motion. Spawns pass `-skillEvent.Facing` as facing.

| Trigger | Effect | Spawn |
|---|---|---|
| `Start` phase | `castEffect` | At the caster, kept for `Cancel` (stops it) |
| `hitAtMs` after Start/Release, projectile rules | `projectileEffect` | Travels caster -> target over `SkillProjectile.FlightMs` |
| `Impact` phase | impact of the charged full rule, the cast's mode, or `impactEffect` | At target (or caster for caster-centred areas); stops the projectile |
| Impact with Companion/Counter flag | `companionEffect` | Travels from 50 units above the caster to the target, arc 12 |
| Impact with Chain flag | `impactEffect` | Travels from the previous link to the target in 0.08 s |
| Status added | `buffEffect`, or `<buffEffect>.<element>` when defined for enchant/companion choices | On the entity; respawned while the status has > 500 ms left; stopped on removal |
| Revive combat event | `vfx.priestess.resurrection.rise` | On the revived entity |

Element variant suffixes are the lowercase `Element` names: `bionic`, `earth`, `fire`, `ice`, `lighting`, `poison`,
`water`, `wind`.

## How to

**Add an effect for a skill**
1. Add a definition to `Client/PristonTale.Gameplay/Effects/Vfx/Classes/<Class>.vfx.json` (or `Common.vfx.json` when
   shared). Use a name unique across all shipped files.
2. Reference it from the skill in `Shared/PristonTale.Simulation/Skills/Data/Classes/<Class>.json` (fields above;
   see docs/skills.md).
3. A new class file is embedded by the `Classes/*.vfx.json` wildcard; no csproj change. Rebuild the client: the data
   is an embedded resource, so edits need a build.
4. Run the regression runner (docs/testing.md): `SkillPresentationTests` checks parsing, unique names, that every
   rule's effect exists and, with `assets/` present, that textures and legacy scripts load. Inspect visually with the
   `--capture-skills <assets> <content> <output> [codes]` probe.

**Override an effect locally**: put an array of definitions in `<assetRoot>/Vfx/<any>.vfx.json` using the shipped
`name`. The whole definition is replaced. It loads when the game scene builds its renderer, so restart the client
after edits. `assets/` is not in Git, so overrides stay local; move them into the shipped files to keep them.

**Add a builtin texture**: add `Add(device, "<name>", (x, y) => alpha)` in the `VfxBuiltinTextures` constructor
(x, y in -1..1, return 0..1) and the name to `VfxBuiltinTextures.Names`, which validation checks. Update the builtin
table here. Check the look by capture.

**Add a property to the schema**
1. `VfxDefinition.cs`: add an `init` property with its default to the layer record; extend `Validate`.
2. Precompute in `CompiledEmitter`/`CompiledTrail` (`VfxCompiled.cs`) when it is derived per definition.
3. Simulation in `VfxSystem.cs` (`EmitterState`, `TrailState`, `VfxInstance`) or vertex output in `VfxRenderer.cs`.
   A new per-vertex value can use the unused `Params.y`; a shader change touches `Vfx.fx` and `PristonVfxEffect`
   (docs/shaders.md).
4. Map it in `LegacyVfxImporter` if the original scripts have an equivalent.
5. Add cases to `tests/PristonTale.Tests/SkillPresentationTests.cs` (`DefinitionsAsync` for rejection,
   `SystemAsync` for behaviour) and update the schema tables in this file.

## Pitfalls

- Property names are case-sensitive camelCase and unknown properties are ignored: a typo (`"Rate"`, `"lifeTime"`)
  silently falls back to the default.
- A misspelled `builtin:` name fails validation; a missing asset texture renders a white square (fallback), not
  nothing.
- `spread` 0 with a `Ring`, `Disc` or `Sphere` shape emits radially and ignores `direction`; set `spread` >= 1 to aim.
- `size` is the half-extent of the quad; trail `width` is the full width. Legacy and importer sizes are halved.
- `drag` (VFX) is linear per second; `Damping` (legacy emitter) is exponential. They are not interchangeable.
- `Ground` alignment uses world axes and ignores facing; rotate it with `rotation`/`spin`.
- Alpha sorting is per instance by caster position; overlapping alpha layers inside one effect draw in emission order.
- Light colours above 1 clamp (8-bit `Color`); use `power` for brighter lights. Lights do not follow anchors or bones.
- `Stop()` before a layer's delay cancels that light, sound or legacy layer for good.
- Sound and legacy layers are not validated; a wrong script name only logs `[Effects] legacy script ... could not be
  read` at spawn time. Legacy layers are skipped when the legacy engine has 128 instances.
- Shipped effect names must be unique across files (asserted); duplicates are only legal as `assets/Vfx` overrides.
- The `LegacyParticleScript` Z axis is negated relative to the scene; keep that when converting coordinates by hand.

## Related

- [rendering.md](rendering.md) — frame graph, depth pre-pass, HDR scene target, transient lights.
- [shaders.md](shaders.md) — `Vfx.fx`, unlit shader, colour encoding.
- [skills.md](skills.md) — skill rules, events and timing that trigger effects.
- [client.md](client.md) — `GameScene`, asset root discovery, client update/draw loop.
- [testing.md](testing.md) — regression runner, `--capture-skills` and other capture probes.
