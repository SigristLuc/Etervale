# Effects and VFX

Particle and skill-effect runtime of the client: `Client/PristonTale.Effects/` (native VFX system, legacy particle
engine), the effect data under `<assetRoot>/Effect/Vfx/`, the converter `tools/PristonTale.VfxImport` (`ptvfx`) that
writes that data, and the gameplay glue in `Client/PristonTale.Gameplay/Effects/` that turns skill events into effects.

## Read this when

- Adding or tuning the visual effect of a skill (cast, projectile, impact, aura, weapon band).
- Porting the effects of a class, an item or a monster from the original game data.
- Adding a field to the `.vfx.json` format, a builtin texture, or a new layer behaviour.
- Debugging an effect that is invisible, a white square, stuck alive, at the wrong position or on the wrong bone.
- Working on potion, meteor, character-select or opening effects that use the legacy `EffectsEngine`.

## Code map

| Path | Responsibility |
|---|---|
| [`Client/PristonTale.Effects/Vfx/VfxDefinition.cs`](../Client/PristonTale.Effects/Vfx/VfxDefinition.cs) | Effect schema (emitters, trails, lights, sounds, children), JSON options, `ParseFile`, `Validate` |
| `Client/PristonTale.Effects/Vfx/VfxComponentDefinition.cs`, `VfxComponents.cs` | Component layer: models, quads and particle pools with keyed events |
| `Client/PristonTale.Effects/Vfx/VfxSequenceDefinition.cs`, `VfxSequences.cs` | Sequence layer: keyed particle streams |
| `Client/PristonTale.Effects/Vfx/VfxPartDefinition.cs`, `VfxParts.cs` | Part layer: animated billboards, streaks, ribbons, weapon bands, beams and models that move, orbit and fly to goals |
| `Client/PristonTale.Effects/Vfx/VfxCatalog.cs` | Reads every `*.vfx.json` under `<assetRoot>/Effect/Vfx` and lists the textures they use |
| `Client/PristonTale.Effects/Vfx/VfxLibrary.cs`, `VfxCompiled.cs` | Name -> `CompiledVfx` registry; `IVfxResources` (textures, models, animated billboards) |
| [`Client/PristonTale.Effects/Vfx/VfxSystem.cs`](../Client/PristonTale.Effects/Vfx/VfxSystem.cs) | `VfxSystem`, `VfxInstance`, emitter and trail simulation, `IVfxAnchors`, `IVfxServices` |
| `Client/PristonTale.Effects/Vfx/VfxRenderer*.cs` | Quads of every layer in one dynamic vertex buffer; effect models through their own draws |
| `Client/PristonTale.Effects/Vfx/VfxBuiltinTextures.cs` | Procedural `builtin:` sprite textures |
| `Client/PristonTale.Effects/Legacy/BillboardSequence.cs` | Original animated billboards (`Effect/AnimationData/*.ini`) and their tick-exact player |
| `Client/PristonTale.Effects/Legacy/`, `EffectsEngine.cs`, `Effects/`, `Particles/`, `Rendering/` | Legacy particle engine used by potions, meteors, character select and the opening |
| [`Client/PristonTale.Gameplay/Effects/VfxContent.cs`](../Client/PristonTale.Gameplay/Effects/VfxContent.cs) | Loads the catalog, models, animations and textures on a worker; registers them on the game thread in 2 ms slices |
| [`Client/PristonTale.Gameplay/Effects/SkillPresentation.cs`](../Client/PristonTale.Gameplay/Effects/SkillPresentation.cs) | Implements `IVfxServices`, maps skill events and statuses to effects |
| `Client/PristonTale.Gameplay/Effects/ScriptEffects.cs` | `IScriptEffects`: monster and meteor code start converted scripts (`particle.<name>`) through the VFX system |
| `Client/PristonTale.Engine3D/Rendering/PristonVfxEffect.cs`, `PristonMeshEffect.cs` | VFX shader wrapper (`Vfx.fx`, blend modes) and baked effect models |
| [`tools/PristonTale.VfxImport/`](../tools/PristonTale.VfxImport) | `ptvfx import`: converts the original effect scripts and installs the hand-authored definitions |
| `tools/PristonTale.VfxImport/Authored/` | Hand-authored definitions (source of `Effect/Vfx/Common` and `Effect/Vfx/Classes`) |
| `tests/PristonTale.Tests/SkillPresentationTests.cs`, `VfxLayerTests.cs`, `VfxImportTests.cs` | Regression cases; `VfxCaptureProbe.cs` and `SkillCaptureProbe.cs` are GPU capture probes |

## Where effect data lives

Effect definitions are game data: the client reads `<assetRoot>/Effect/Vfx/**/*.vfx.json` and nothing is embedded in
the client. `assets/` is not in Git, so the data is produced by `ptvfx import <assetRoot>` (docs/tools.md):

| Folder under `Effect/Vfx/` | Content | Source |
|---|---|---|
| `Common/` | Shared skill building blocks: hit flashes, impact bursts, weapon bands, stun stars | `tools/PristonTale.VfxImport/Authored/Common/` |
| `Classes/<Class>/` | One file per class with every effect its skill rules name | `tools/PristonTale.VfxImport/Authored/Classes/<Class>/` |
| `Scripts/` | `script.<File>`: one per original effect group script (`Effect/NewEffect/*.lua`) | converted |
| `Particles/` | `particle.<File>`: one per original particle script (`Effect/Particle/Script/*.part`) | converted |
| `Items/`, `Monsters/`, `Costumes/` | Reserved for item, monster and costume effects authored the same way | `Authored/<Folder>/` |

Files load in ordinal order of their relative path. A name defined again replaces the earlier definition (reported as
an error by the catalog, so shipped data keeps names unique). A file that cannot be read, parsed or validated is
reported (`[Effects] <file>: <reason>`) and skipped as a whole; the other files still load.

Loading: `LegacyEffectLibrary.LoadBootstrap` creates `VfxContent`, whose worker reads the catalog, parses effect models
(with their sibling `.smb` skeleton), reads animated billboards and decodes every texture. `SkillPresentation.Update`
then registers prepared definitions on the game thread for at most 2 ms per frame and never waits for the worker;
effects requested before their registration are skipped. Probes and tools call `VfxContent.CompileAll()`.

## Units, colours and blends

- Positions and sizes in world units, Y up; effect space has -Z forward. Offsets, directions and orbits turn with the
  spawn facing (`VfxInstance.Orientation`) unless a layer says otherwise. Times in seconds, angles in degrees.
- Emitter and trail colours are linear Rec.709 floats scaled by `intensity`. Component, sequence and part colours are
  the original 0..255 display values; they are decoded from sRGB to linear when drawn, alpha stays linear.
- Original rates per 1/70 s tick convert by x70 (speeds) and x4900 (accelerations); 1/256-unit fixed values divide by
  256 first. Animated billboards and part motion advance in 70 Hz ticks.
- Sizes of components, sequences and parts are full extents; emitter `size` is a half extent.

| `blend` | Draw | Shader mode |
|---|---|---|
| `Additive` | source x alpha + destination (lamp) | radiance |
| `Alpha` | over, non-premultiplied | radiance |
| `Color`, `AddColor` | source + destination (approximates source x source) | squared radiance |
| `Shadow` | destination x source | multiplier |
| `InvShadow` | destination x (1 - source) | inverse multiplier |

The renderer draws alpha layers first (instances back to front), then the darkening layers, then the light layers.
Effect models draw before the quads with their own blend.

## Effect (`VfxDefinition`)

| JSON | Type | Default | Meaning |
|---|---|---|---|
| `name` | string | required | Unique key (case-insensitive). `vfx.<class>.<skill>.<role>`, `vfx.common.<name>`, `script.<File>`, `particle.<File>` |
| `duration` | float s | 1 | `0 < duration <= 600`: layers that run until stopped stop then; the instance ends when every layer has ended |
| `emitters`, `trails` | arrays | [] | Sprite emitters and anchor ribbons (tables below) |
| `lights`, `sounds` | arrays | [] | One-shot transient lights and sounds |
| `components`, `sequences` | arrays | [] | Layers of the converted original scripts |
| `parts` | array | [] | Original-style effect objects (the skill ports use them) |
| `children` | array | [] | Other named effects started at an anchor |

An instance stays alive while any layer runs or a one-shot is still due. `Stop()` ends emission: waiting parts and
one-shots never start, layers that run until stopped end, finite ones finish.

### Emitter, trail, light, sound

| Layer | Fields (default) |
|---|---|
| emitter | `texture`, `frameColumns`/`frameRows` (1), `blend`, `intensity` (1), `softness` (16), `anchor` (Point), `bone`, `offset`, `local`, `delay`, `emitTime` (0 = whole duration), `rate`, `burst`, `maxParticles` (64), `lifetime` [1,1], `speed`, `direction` [0,1,0], `spread` deg, `shape` (Point/Sphere/Ring/Disc), `radius`, `gravity`, `drag`, `size` keys (half extent), `color` keys (linear RGBA), `rotation`, `spin`, `align` (Camera/Ground/Velocity), `stretch` (0.02) |
| trail | `texture`, `blend`, `intensity`, `anchor` (Caster), `bone`, `offset`, `width` (12, full), `lifetime` (0.25 per point), `delay`, `emitTime`, `color` keys head -> tail |
| light | `anchor`, `offset` [0,30,0], `color` [r,g,b] 0..1, `power` (160), `decay` (4 per tick), `delay`; legacy light units (radius power/2, brightness power x colour / 256) |
| sound | `path` (asset-relative wave), `delay`, `volume` (1); played at the caster |

A key is `{ "t": 0..1, "v": [..] }` with non-decreasing `t`, sampled linearly and clamped.

### Child (`VfxChildDefinition`)

`effect` (name), `anchor`, `bone`, `offset`, `delay`, `follow` (keep the child's spawn point on the anchor), `turn`
(give it this effect's facing), `stopWithParent` (true). The child is looked up by name on first use, so it may live in
any file.

### Component (`VfxComponentDefinition`)

One element of a converted effect group with its own clock, loops and keyed events: `kind` (`Model`, `Billboard`,
`Ground`, `Beam`, `Particles`), `anchor`, `bone`, `follow`, `offset`, `offsetTurn` (None/Facing/Reverse), `texture`,
`frames` + `frameTime` (numbered textures `<stem>1.<ext>`..), `model`, `skeleton`, `modelFrames` (30 per second),
`loop` (0 = until stopped), `delay`, `endTime` (quads: seconds per loop; particles: lifetime range), `color` (0..255),
`sizeMin`/`sizeMax`, `blend`, `beamFrom`/`beamTo`, particle `spawn` (Point/Box/Sphere/Doughnut) with
`spawnMin`/`spawnMax`, `count`, `rate`, `velocityMin`/`velocityMax`, `outward`, `beams`, and `events`
`[t, kind Color|Size, ramp, value]`. A set event replaces the value; a ramp event is a target that the event before
it moves towards, and the last step keeps going after the last ramp. Every loop restarts the events at 0 without
resetting values. Particle pools hold at most 100 live particles.

### Sequence (`VfxSequenceDefinition`)

A converted particle stream: `texture`, `blend`, `shape` (Billboard, Ground, Upright, Trail), `count` live at most,
`loops` (0 = until stopped), `rate` range drawn each frame, `delay`, `lifetime`, offset box `radiusMin`/`radiusMax`,
gravity box (drawn every frame), `anchor`, `bone`, `offset`, `follow`, and `events` (`t`, `ramp`, `set`, `min`, `max`,
`mode`). Properties: `Size`, `Stretch`, `Color`, `Red`..`Alpha`, `Velocity` (Vector, Outward, Inward) and its axes,
`Angle` and axes, `Orbit` and axes (turn the particle's offset), `Clock` (restart the event clock at a time).

### Part (`VfxPartDefinition`)

A group of `count` objects that start at `delay + delayStep x index + random(delayRandom)` and live for `lifetime`
seconds; a zero lifetime lives as long as its animation, model clip or flight, or until the effect stops.

| Group | Fields |
|---|---|
| Look | `shape`: `Billboard`, `Ground`, `Streak` (from where it started to where it is), `Line` (behind it along its velocity, `lineSeconds` long), `Ribbon` (through its last `trailPoints`, `trailFade` alpha off per point), `Blade` (band swept by a bone over `bladeLength` along its Y axis), `Beam` (to its goal, or to `beamTo`), `Model` |
| Image | `animation` (an `Effect/AnimationData` file: images, hold ticks, opacity, size, angle and blend come from it), `loop`, `sized` (start from `size` instead of the file's sizes), or `texture` with `frames`, `frameTime`, `pingPong`; `blend`, `color` (0..255), `size`, `height`, `growth`, `rotation`, `spin`, `width` (half width of strips) |
| Model | `model`, `skeleton`, `modelStart`, `modelFrames`, `modelSpeed` (clip frames per second), `loopFrom`, `angles`, `tumble`, `scale`, `pulse` + `pulsePeriod`, `faceGoal` (face the velocity) |
| Fade | `fade`: `Rise` (up over 25.5 % of the life, down at the same rate), `Fall` (down over the last 25.5 %), `InOut` (`fadeIn`, `fadeOut` seconds); `fadeOut` also times the fade after arriving |
| Place | `anchor`, `bone`, `boneWorld` (full bone transform, offset in bone space), `offset`, `follow`, `turn` (true), `jitterMin`/`jitterMax`, `ring` radius with `ringStart` + `ringStep` x index or `ringRandom`, `shell` (ellipsoid radii) |
| Motion | `radial` (outward along the ring angle), `rise`, `velocityMin`/`velocityMax`, `burst` (along the shell direction), `gravity`, `swirl` (deg/s about the vertical), `moveTime` (stop after) |
| Goal | `goal` (Caster, Target, Point) + `goalOffset`, `speed`, `reach`, `lift` (lob under `gravity`), `end`: `Vanish`, `LastFrame` (keep flying through the last frame or `fadeOut`), `Hold` (stop and fade out) |
| Orbit | `orbitRadius`, `orbitSpeed`, `orbitTilt`, `orbitRise`, `orbitGrowth`, `orbitPhase` + `orbitPhaseStep`, `orbitAlternate`; with a goal the orbit starts at the goal's bearing and distance |
| Effects | `endEffect` (where a part arrives or its life ends), `everyEffect` every `every` seconds at each part, `carryEffect` (started at birth, moved with the part, stopped when it ends) |

Use `carryEffect` for converted particle scripts that stream until stopped; `everyEffect` only for finite effects.

## Runtime: `VfxSystem`

- `Spawn(CompiledVfx, VfxSpawn(CasterId, TargetId, Point, Facing))` returns a pooled instance, or null at 1024 live
  instances. `Update(seconds)` clamps the step to 0.1 s, refreshes caster and target positions, updates every layer,
  fires one-shots and children, and returns dead instances to the pool.
- `Travel(from, to, seconds, arc)` moves `Point` (projectiles, chain links, companion strikes) and stops the instance on
  arrival; parts with `follow` ride it, parts with a `goal` fly on their own and finish their flight after a stop.
- Anchors: `Point` is the spawn point; `Caster`/`Target` the entity position, replaced by a bone through
  `IVfxAnchors.TryGetBone` / `TryGetBoneWorld`. `GameScene` and the probes implement them from the actor bones.
- Memory: instances, parts, trails and pools are reused; after warm-up an update allocates nothing (asserted).
- Threading: `SkillPresentation.Update` and `Draw` run on the game thread; the system has no locks.

## Converting the original scripts (`ptvfx`)

`ptvfx import <assetRoot>` converts every effect group script to `Effect/Vfx/Scripts/<File>.vfx.json` (`script.<File>`)
and every particle script to `Effect/Vfx/Particles/<File>.vfx.json` (`particle.<File>`), installs `Authored/**`, and
prints every construct it kept with a caveat. Effect group scripts become components (Mesh -> `Model`, Billboard ->
`Billboard`, CreateMesh -> `Ground`, BillboardAxial -> `Beam`, ParticleSystem -> `Particles`, Parent -> offset) with
the original defaults (particles: loop 1, 50 per loop, 30 per second, life 1..2 s, size 5..10). Particle scripts keep
the original tokenizer: a value glued to `=` reads as 0, a script the original rejects plays nothing. Axes: Z is
negated and rotations about X and Y are mirrored. Remaining caveats of the shipped data (reported, not mapped): models
and animated textures the game data lacks (those components are left out), a particle system's start delay, and
values the original also fails to read.

## Skill events to effects (`SkillPresentation`)

Skill rules (`Shared/PristonTale.Simulation/Skills/Data/Classes/<Class>.json`) name effects in `castEffect`,
`impactEffect`, `projectileEffect`, `buffEffect`, `companionEffect`, `modes[i].impactEffect` and
`charge.full.impactEffect`.

| Trigger | Effect | Spawn |
|---|---|---|
| `Start` phase | `castEffect` | At the caster; stopped on `Cancel` |
| `hitAtMs` after Start/Release, projectile rules | `projectileEffect` | Travels caster -> target |
| `Impact` phase | charged, mode or plain `impactEffect` | Point at the target (or caster for caster-centred areas), target id set |
| Companion/Counter impact | `companionEffect` | Travels from above the caster to the target |
| Chain impact | `impactEffect` | Travels from the previous link to the target |
| Status added | `buffEffect` (or `<buffEffect>.<element>`) | On the entity while the status lasts |
| Revive | `vfx.priestess.resurrection.rise` | On the revived entity |

## Skill effect inventory

What each active skill shows in the original and the effect names that carry it. "Hit" is the weapon point (socket plus
half the weapon length) unless noted; impacts are drawn at the target's chest. Tempskron skills are ported with parts;
the Morion classes still use their earlier definitions plus converted scripts and are next to port (see below).

### Mechanician

| Skill | Original | Effects |
|---|---|---|
| Extreme Shield | Flash, two shield bursts, sparks and two small bursts at the shield; a pulsing glow model on the shield hand while active | `vfx.cast.shield`, `vfx.buff.shield` |
| Mechanic Bomb | Tumbling bomb lobbed with dust behind it; fire burst of shards, rays, two flashes and a red light | `vfx.projectile.bomb`, `vfx.impact.bomb` |
| Physical Absorption | Rising spiral, falling sparks, outward streaks, burst ring, a shell model; two shields orbiting with trails while active | `vfx.cast.absorb`, `vfx.buff.absorb` |
| Great Smash | Blue band; round impact (flash, 20 rays, sparks, blue light) | `vfx.trail.blue`, `vfx.mech.greatsmash.hit` |
| Maximize, Automation | Gathering burst and six pairs of weapon emblems; rising streaks around the caster while active | `vfx.mech.maximize.*`, `vfx.mech.automation.*` |
| Spark | Unseen bolt dropping spark clusters; spark ball and spark streaks at the target | `vfx.mech.spark.*` |
| Metal Armor | Gathering burst; armour model and particle aura while active | `vfx.mech.metalarmor.*` |
| Grand Smash | Bone band; hit particles, rays, green light | `vfx.trail.bone`, `vfx.mech.grandsmash.hit` |
| Spark Shield | Four lightning bolts rising, shield particles, hand glow while active; lightning to the attacker on block | `vfx.mech.sparkshield.*` |
| Impulsion | Weapon glow; rays and hit particles; lightning arcs between chained targets | `vfx.mech.impulsion.*` |
| Compulsion, Metal Golem | Converted effect groups at the caster | `vfx.mech.compulsion.cast`, `vfx.mech.metalgolem.cast` |
| Magnetic Sphere | Orb machine hovering by the caster; lightning and shots to what it strikes | `vfx.mech.magneticsphere.*` |

### Fighter

| Skill | Original | Effects |
|---|---|---|
| Raving, Impact | Coloured band (red, gold); critical flash per hit | `vfx.trail.rage`, `vfx.trail.golden`, `vfx.impact.critical` |
| Triple Impact, Brutal Swing | Violet / green band; triple impact burst | `vfx.trail.violet`, `vfx.trail.green`, `vfx.fighter.tripleimpact.hit`, `vfx.fighter.brutalswing.hit` |
| Roar | Lights, five expanding roar models, dust; stun stars on targets | `vfx.fighter.roar.cast`, `vfx.debuff.stun` |
| Rage of Zecram | Marks and flashes at the cast; fire hit, then an eruption of models, rays and fire trails | `vfx.fighter.zecram.*` |
| Concentration, Swift Axe | Streaks gathering then flying out; particle aura while active | `vfx.fighter.concentration.*`, `vfx.fighter.swiftaxe.*` |
| Avenging Crash, Bone Crash | Hit particles and lights; Bone Crash adds the bone band and a ground smash | `vfx.fighter.avengingcrash.hit`, `vfx.fighter.bonecrash.hit` |
| Destroyer | Long bone band; converted hit group per hit | `vfx.trail.bone.long`, `vfx.fighter.destroyer.hit` |
| Berserker | Converted group and particles on the caster while active | `vfx.fighter.berserker.aura` |
| Cyclone Strike | Long bone band; normal hit on each target | `vfx.fighter.cyclone.*` |

### Pikeman

| Skill | Original | Effects |
|---|---|---|
| Pike Wind | Ring of pikes bursting from the ground and swirling dust | `vfx.area.pikewind` |
| Critical Hit, Jumping Crash | Band; critical flash | `vfx.trail.violet`, `vfx.impact.critical`, `vfx.impact.crash` |
| Ground Pike | Ice pikes racing out in ten directions with sparkles | `vfx.pike.groundpike.hit` |
| Tornado | Two bands sweeping a full turn through the target, two round impacts there | `vfx.pike.tornado.hit` |
| Expansion | Enlarged red weapon (approximated by a wide band); round impact per hit | `vfx.pike.expansion.*` |
| Venom Spear | Particles ahead of the caster; venom model and three particle bursts at each target | `vfx.pike.venomspear.*` |
| Vanish | Flares rushing through the caster and particles | `vfx.pike.vanish.cast` |
| Chain Lance | Red band; hit particles per hit | `vfx.trail.red`, `vfx.pike.chainlance.hit` |
| Assassin Eye | Converted eye group above the marked target | `vfx.pike.assassineye.mark` |
| Charging Strike, Shadow Master | Charge group; triple impact per hit | `vfx.pike.charging.*`, `vfx.pike.shadowmaster.hit` |
| Vague | Body translucency only | (sound only) |

### Archer

| Skill | Original | Effects |
|---|---|---|
| Scout Hawk | Flash and gate; the hawk flying its baked path around the caster | `vfx.cast.hawk`, `vfx.buff.hawk` |
| Wind Arrow, Perfect Aim | Arrow with dust puffs (Perfect Aim adds a streak); critical flash on arrival | `vfx.projectile.wind`, `vfx.projectile.aim`, `vfx.impact.arrow`, `vfx.archer.aim.hit` |
| Falcon, Golden Falcon | Gate of embers and pillars; the falcon hovering above; strike flight and triple impact | `vfx.archer.falcon.*`, `vfx.archer.goldenfalcon.*` |
| Arrow of Rage | Arrow rising over the target and bursting; rain of arrows with dust | `vfx.archer.rage.*` |
| Avalanche | Arrows leaving afterimages; triple impact | `vfx.archer.avalanche.*` |
| Elemental Shot | Arrow carrying fire; fire hit or lightning strike by element | `vfx.archer.elemental.*` |
| Bomb Shot | Javelin carrying sparks; explosion and three flying bursts | `vfx.archer.bombshot.*` |
| Perforation | Spinning shot with two ribbons through the line; twist particles at each victim | `vfx.archer.perforation.*` |
| Recall Wolverine | Fire particles | `vfx.archer.wolverine.cast` |
| Phoenix Shot | Phoenix model flying through the target carrying fire | `vfx.archer.phoenix.shot` |
| Force of Nature | Converted group on the caster while active | `vfx.archer.nature.aura` |

### Knight

| Skill | Original |
|---|---|
| Sword Blast | Blade model flying past the target with sparks |
| Holy Body | Two counter-rotating body models, flash, spiral sparks; two orbiting comets while active |
| Double Crash | Normal or critical hit flashes |
| Holy Valor, Drastic Spirit | Gathering and outgoing streaks; particle aura while active |
| Brandish | Slash model ahead twice; burst of lines, plates and sparks |
| Piercing | Weapon glow and sparkles; piercing model flying forward with particles |
| Divine Inhalation, Godly Shield | Hand burst, outgoing streaks, hand glow; shield model swap |
| Holy Incantation | Hand particles; particles on the target and above a charmed monster |
| Grand Cross | Hit particles per hit and a cross model |
| Sword of Justice | Converted group, ice particles and three models |
| God Bless | Shield particles, streaks and a converted group on the caster while active |
| Divine Piercing | Hit particles per hit |

### Atalanta

| Skill | Original |
|---|---|
| Shield Strike | Shield glow and ground quads; stun stars |
| Farina, Bigger Spear | Javelin with trail faces; burst of lines and flashes on arrival |
| Windy, Frost Javelin | Light, gathering ribbons; aura on the javelin while active (frost shots replace attacks) |
| Twist Javelin, Fire Javelin, Storm Javelin | Rolling javelin model with particle trail; hit particles |
| Soul Sucker | Rising souls streaming to the caster |
| Split Javelin | Several javelins hovering then flying |
| Triumph / Hall of Valhalla | Hand aura; converted group |
| Lightning Javelin | Lightning strip projectile; homing sparks and lightning particles |
| X-Rage, Vengeance | Shot model with ribbon; converted hit group |

### Priestess and Magician

Every cast draws a cast circle at the feet (circle model plus two ground quads; the Magician has its own textures).

| Class | Skills |
|---|---|
| Priestess | Healing (spiral model and particles), Holy Bolt (tracking particle stream, line burst), Multi Spark (fanning sparks), Holy Mind, Divine Lightning (lightning strike per target), Holy Reflection (petals, net models), Grand Healing (wing models, orbiting ribbons), Vigor Ball (homing balls), Resurrection, Extinction, Virtual Life, Glacial Spike, Regeneration Field, Chain Lightning (beams between targets), Summon Muspell |
| Magician | Agony (spiral), Fire Bolt and Fire Ball (tracking stream, meteor shards), Zenith (orbiting upright quads), Watornado (water column model), Enchant Weapon (weapon particles by element), Dead Ray (beam ribbon), Energy Shield (model and particles), Diastrophism (darkening light, model), Spirit Elemental, Dancing Sword, Fire Elemental, Flame Wave, Distortion, Meteo (four falling meteors) |

### Porting the Morion classes

The machinery is complete for these classes; a port is data only:

1. Read the class's skills above and the original behaviour; express each element as parts in
   `tools/PristonTale.VfxImport/Authored/Classes/<Class>/<Class>.vfx.json`, reusing `vfx.common.*` for hit flashes and
   bursts and `script.*` / `particle.*` children (or `carryEffect`) for the original scripts.
2. Map elements: animated billboards -> `animation`; streaks and rays -> `Streak`/`Line` with `burst`/`radial`;
   orbiting comets -> `Ribbon` + billboard parts with `orbit*`; projectiles -> parts with a `goal` and `endEffect`;
   javelins and meteors -> `Model` parts with `faceGoal`; weapon glows -> children on `Bip weapon01`; cast circles ->
   `Model` + `Ground` parts; beams between targets -> `Beam` parts with a goal.
3. Keep the effect names the rules use or update the rule fields; run `ptvfx import`, the regression runner and the
   capture probes, and look at the frames.

## Limitations

- Actor appearance changes are not drawn: body tints (anger, poison, frost), translucency (Vague, Vanish), afterimages
  (Shadow Master), shield model swaps (Spark Shield, Divine Inhalation, Godly Shield) and the enlarged weapon of
  Expansion (approximated by a wide band).
- Weapon bands use a fixed blade length per effect, not the equipped weapon's length.
- Per-hit sound variants (Chain Lance, Shadow Master) play the rule's single impact sound.
- `Color`/`AddColor` blends draw additively with squared colour; `Shadow`/`InvShadow` darken in the HDR scene.

## Pitfalls

- Property names are camelCase and unknown properties are ignored: a typo silently falls back to the default.
- A missing texture draws the white fallback; a missing animated billboard or model draws nothing.
- Parts that run until the effect stops keep a buff alive; give one-shot parts a lifetime, an animation or a goal.
- `everyEffect` with a converted particle script that streams until stopped leaks for ten minutes; use `carryEffect`.
- Shipped names must be unique across files; the catalog reports a replaced name as an error.

## Related

- [rendering.md](rendering.md) - frame graph, HDR scene target, transient lights.
- [shaders.md](shaders.md) - `Vfx.fx` modes, colour encoding.
- [skills.md](skills.md) - skill rules and events that trigger effects.
- [tools.md](tools.md) - `ptvfx`.
- [testing.md](testing.md) - regression runner, `--capture-vfx` and `--capture-skills`.
