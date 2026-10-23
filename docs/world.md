# World rules

Shared world rules the authoritative server runs: fields and their catalog, stage collision and ground queries,
movement checks, monsters and their AI, spawning, loot, motion timing and NPC services. Covers
`Shared/PristonTale.Simulation/World/`, `Shared/PristonTale.Simulation/Physics/` and `Shared/PristonTale.Geometry/`.
How the tick calls these rules belongs to [application.md](application.md).

## Read this when

- You add or change a field: its stage, gates, warp gates, level limit or start points.
- A monster gets stuck, walks through walls, stands on a roof or floats, or a player movement report is refused.
- You change how monsters notice, chase, attack, flinch, return to their spawn area or move as summons.
- You change spawn density, group size, despawn timing or the scheduled boss spawns.
- You change drop tables, gold rolls, kill experience or kill credit.
- Attack, flinch or potion timing does not match the animation (INX motion data).
- You add an NPC service, a shop rule, a warehouse limit or a teleport route.

## Code map

| Path | Responsibility |
|---|---|
| [`WorldDefinitions.cs`](../Shared/PristonTale.Simulation/World/WorldDefinitions.cs) | `FieldDef`, its records (gates, warp gates, ambients, lighting, camera, day/night), `FieldState`, `WorldCatalog` |
| `World/Fields/*.cs` | One static `Definition` per field, grouped by region (`DungeonFields`, `IceFields`...); `FieldSpawnData` records |
| `World/FieldBackdrops.cs` | Sky backdrop texture keys (day, glow, night) shared by fields |
| [`World/FieldTerrain.cs`](../Shared/PristonTale.Simulation/World/FieldTerrain.cs) | Ground queries for one field: place on floor, step, wall crossing, wall slide, body sweep |
| `World/MonsterStepping.cs` | Server monster step: full step, then two side probes; `IStepOccupancy` |
| `World/WorldPosition.cs` | `IsValid`: finite and within ±1,000,000 units on every axis |
| `World/Monsters/` | `MonsterDefinition` (stats, AI block, skill block, drops), `MonsterCatalog`, `MonsterInstance` (live state), `MonsterMotionState` |
| [`World/Ai/MonsterAiBrain.cs`](../Shared/PristonTale.Simulation/World/Ai/MonsterAiBrain.cs) | Wild monster state machine: target scan, chase, attack, leash and stuck manoeuvres, flinch, boss retarget |
| `World/Ai/MonsterAiTuning.cs` | Per-definition derived constants and clip timings; global AI constants |
| `World/Ai/SummonBrain.cs` | Owned summon behaviour (follow owner, attack ordered target) |
| `World/Ai/MonsterLocomotion.cs` | Monster walk/run speed |
| `World/Ai/IPreyView.cs`, `ISimRng.cs`, `MonsterAttackEvent.cs` | Seams the host implements: prey list, random source, landed swing |
| `World/Spawning/FieldSpawner.cs` | Population pass, idle despawn, scheduled bosses; `SpawnSeams.cs` holds `IPlayerProximity`, `ISpawnSink` |
| `World/Spawning/SpawnCadence.cs` | Optional bitmask spawn cadence on a 70 Hz frame clock |
| `World/Spawning/BossSpawnSchedule.cs` | Server-wide hourly boss pass at a random minute per day |
| `World/Loot/` | `DropTable`, `DropRoller`, `KillReward` (experience and kill credit), `GroundDropView`/`GroundDropHandle` |
| `World/Motion/` | `MotionClip`, `MotionStateCode`, `MotionTiming`, `MotionTimingSet`/`MotionTimingCatalog`, player fallbacks |
| `World/Motion/PlayerEquipmentTiming.cs` | Generated table of player attack clips by job and weapon key; do not hand-edit |
| `World/Npcs/` | `NpcDefinition`, `NpcCatalog`, typed services (`NpcServiceKind`), service commands and replies, shop and warehouse rules, teleport routes, sound code map |
| `Physics/EntityCollision.cs` | Circle-vs-circle blocking between bodies (players, monsters, NPCs) along a swept segment |
| `Physics/EntityCollisionRadii.cs`, `EntityNeighbour.cs`, `EntityCollisionKind.cs` | Body radii and neighbour records |
| `Physics/SpatialIndex.cs` | XZ hash grid of entity positions with deterministic query order |
| [`Geometry/StageCollisionReader.cs`](../Shared/PristonTale.Geometry/StageCollisionReader.cs) | Reads collision triangles from a stage `.smd` |
| `Geometry/CollisionMesh.cs` | Immutable triangle grid: floor height, floor raycast, segment trace |
| `Geometry/AssetPath.cs` | Case-insensitive asset path resolution and assets root discovery |

`PristonTale.Geometry` has no dependencies and is referenced by `PristonTale.Simulation` and the client's
`PristonTale.Engine3D` (`Physics/PristonCollisionWorld.cs` wraps the same `CollisionMesh` in MonoGame space).

## Coordinates and units

| Quantity | Convention |
|---|---|
| World position | `Vector3` in Priston world units, stage file axes, Y up. The server and all rules here use these axes. |
| Fixed point | Stage vertices and NPC placements are stored ×256; divide by 256 to get world units. |
| Client space | MonoGame `(x, y, -z)`; conversion happens in client code, never in these rules. |
| Facing | Radians; forward is `(sin f, 0, cos f)`, so `f = atan2(dx, dz)`. |
| Authored and wire angles | 4096 steps per turn (`(angle & 4095) * Tau / 4096`). Monster side probes and stuck turns use this circle. |
| Time | Rules take `dtMs` (float milliseconds) or a field clock in ms; no wall clock except the boss schedule. |
| Distances | Most checks are planar (XZ) with a separate vertical gate; aggro uses full 3D distance. |

Coordinates in field definitions: `FieldPoint`, `FieldGate`, `FieldWarpGate` and `SpawnPoint` are integer world units.
`FixedNpc` X/Y/Z are fixed point (`WorldNpc.Place` divides by 256). `FieldLight` is in client engine units and is
presentation only. Spawn points carry no height: the host places them on the highest floor.

`PlayerNearRadiusSquared = 0x120000` (about 1086 units, in `FieldInstance`) is the "a player is near" radius for
spawning and despawning; the monster skill area test uses the same value.

## Fields

`FieldDef` is a code-defined record, one static `Definition` per field in `World/Fields/`. `WorldCatalog()` lists every
field; `WorldCatalog(IEnumerable<FieldDef>)` builds test catalogs. Lookups by `Id` (string, the stage base name such as
`village-2`) and by `FieldCode` (int). The server keys live fields by `FieldCode`; both must be unique.

| Member | Use |
|---|---|
| `StageSmd` | Asset-relative stage path with Windows separators; source of collision and of the importer's join key |
| `StageObjectSmds` | Animated stage props; client only |
| `State` (`FieldState`) | `Village` sets the player's in-village flag on entry; `Dungeon` sets `Underground` (no sky) |
| `Center`, `StartPoints` | `StartPoints[0]` is the respawn point and the NPC teleport arrival; `Center` is the teleport fallback |
| `Gates` | Seamless boundaries to an adjacent field (`DestinationFieldId`, X/Z, angle) |
| `WarpGates` | Portals: trigger X/Z/Y and Width/Height, destination field and X/Z/Y, `LimitLevel`, `SpecialEffect` |
| `LimitLevel` | Minimum character level to enter (gates, warps and teleports) |
| `Ambients`, `Lighting`, `Camera`, `DayNight`, `Backdrop`, `BackMusicCode`, `OnLoaded`, `OnUpdate` | Client presentation |
| `ServerCode`, `FieldEvent` | Authored labels; no runtime rule reads them |
| `Spawns` | Defaults to empty and is not used by the server; spawn data comes from the database (see Spawning) |

`WorldCatalog.Neighbours(field)` returns fields linked by a gate in either direction; the client uses it to stream
adjacent stages.

### Field transitions

`FieldTransition.TryResolve` (in `Shared/PristonTale.Application/World/`) authorizes a field change from the player's
server position; the requested position is never trusted. Rules:

- Refused for an invalid origin, the same field, or `level < destination.LimitLevel`.
- Warp: a source warp gate to the destination, `SpecialEffect != 2`, `level >= warp.LimitLevel`, origin within
  `max(Width, 48) + 64` planar units of the trigger. Arrival is the warp's authored destination point, settled on the
  floor within 64 units.
- Gate: a gate between the two fields (either direction) within 1810 planar units of the origin. The player keeps the
  position.

## Stage collision

### Stage file subset

`StageCollisionReader.Read(byte[])` reads only what collision needs. All integers little endian.

| Part | Layout |
|---|---|
| File header | 556 bytes. ASCII signature in the first 24 bytes: `SMD Stage data Ver 0.72` or `... 0.73`. Material count (int32) at offset 28. |
| Stage block | 262,260 bytes after the header. Vertex count and face count (int32 each) at block offset `4 + 256*256*4 + 48`. |
| Material group | 88-byte header; the declared material count (uint32 at +8) must equal the file header count. |
| Material | 320 bytes: in-use flag (uint32 at 0), state flags (uint32 at 168; bit 0 = check face). |
| Name table | Follows each in-use material only: int32 length, then that many bytes of texture names. |
| Vertex | 28 bytes; X, Y, Z as int32 fixed point at offsets 8, 12, 16. |
| Face | 28 bytes; vertex indices (uint16) at 8, 10, 12; material index (uint16) at 14. |

Only faces whose material has the check-face bit become triangles; faces with out-of-range indices are skipped.
Malformed files throw `InvalidDataException`.

### CollisionMesh

- Every check-face triangle blocks (segment traces); triangles with `|normal.Y| >= 0.25` are also floors.
- Two XZ grids (floors, all triangles). Cell size `sqrt(area * 8 / triangles)` clamped to 32-512 units, one spare
  cell on each side; a triangle is registered in every cell its bounds overlap.
- `TryGetFloorHeight(x, z)` returns the highest floor under the point; the overload with `referenceHeight` and
  `stepUp` ignores floors above `referenceHeight + stepUp` (ceilings, upper storeys).
- `TryTraceSegment` returns the nearest hit and the triangle normal. The normal is unoriented (follows winding).
- Immutable after construction, so one mesh serves concurrent queries from parallel field steps.

### FieldTerrain

One per field; wraps an optional mesh. `FieldTerrain.None` (no mesh) accepts every query and keeps the caller's
height. Constants: `StepUp = 10`, `BodyProbeHeight = 30`, `DropTolerance = 32`.

| Method | Rule |
|---|---|
| `PlaceOnFloor(p)` | Highest floor under X/Z, any height. Used for spawns, summons, start and respawn points. |
| `TryFloorNear(p, climb, out h)` | Highest floor at or below `p.Y + climb`. |
| `SettleNear(p, tol)` | Snaps to a floor in `[p.Y - tol, p.Y + tol]`, else keeps `p`. Used for drops, knockback, warp arrival. |
| `CrossesWall(a, b)` | Segment from `a` to `b` (at `a`'s height), both raised 30 units, hits any collision triangle. |
| `TryStep(a, b)` | No wall crossing and a floor within `StepUp` above `a.Y`; falls are allowed. NPC steps use it. |
| `TryBodyStep(a, b, halfWidth, stepUp)` | Centre path leads by `halfWidth`; both flanks swept. A flank may end nearer a wall only if it started nearer. |
| `Slide(a, b)` | Replica of the client wall slide: stop 0.5 units before a wall, project the rest along it, three tries; faces as flat as floors do not stop it. |

### Loading

The server's `FieldTerrainLoadService` (`Server/PristonTale.Server/Hosting/`) reads every field's `StageSmd` in
parallel at startup and publishes them once to `FieldTerrainCatalog` (Application). A missing assets root or stage stops
startup unless `Assets:AllowMissingTerrain=true`, which leaves those fields on `FieldTerrain.None`. See
[server.md](server.md).

## Movement and body collision

| Mover | Rule | Code |
|---|---|---|
| Player (server check of a report) | Distance budget, wall check or slide, a floor at most 64 units above; height comes from the server floor | `WorldPlayer.ReportMovement` (Application) |
| Wild monster and summon | `MonsterStepping.Step` against stage and other monsters only | `FieldInstance.MoveMonster` |
| NPC | `EntityCollision.Resolve` against all bodies, then `FieldTerrain.TryStep` | `FieldInstance.StepNpc` |
| Local player (client prediction) | `EntityCollision.Resolve` with player radius, then client terrain | `Client/PristonTale.Gameplay/World/GameScene.cs` |

`MonsterStepping.Step(terrain, from, heading, distance, occupancy, out accepted)`:

1. Probe the requested heading at the full distance with `TryBodyStep(halfWidth 12, stepUp 20)`.
2. Else probe heading -768 and +768 steps (67.5 degrees) at half the distance. Facing is not changed by the host.
3. Result `Moved`, `Slid`, `Crowded` (every terrain-legal probe ended on another monster) or `Blocked`.

Occupancy is monster against monster only: another live monster within 32 units planar and 32 vertical. A monster
already overlapping another may walk out. `Crowded` steps do not count as stuck in the AI.

`EntityCollision.Resolve` is all or nothing: it returns the old position if the swept segment newly enters any
neighbour circle (vertical overlap within the radius), and allows leaving an existing overlap. The pair distance is
`min(moverRadius, neighbourRadius)`, not the sum. Radii: player 16, monster 32, NPC 32.

`SpatialIndex` is a hash grid (default 256-unit cells, cell indices clamped to ±1,000,000). `Put` keeps an entry's
first `Order`; `Query` returns candidates sorted by `Order`, which keeps neighbour iteration deterministic.

## Monsters

`MonsterDefinition` is built from the database by `WorldDataFactory.ToMonsterDefinitions`
(`Persistence/PristonTale.Persistence.Abstractions/WorldData/`):

- `Code` is the 1-based position of the record in the loaded list; 0 means unset. Spawn tables and boss spawns
  reference monsters by name and are resolved to codes at load, so codes are stable only within one load.
- `Faction` is always `Enemy`; `Name` is the display name (`"Monster"` when empty); `ModelInxPath` is normalized to
  `.inx`.
- `Combat.MeleeRange` is the attack reach; `Combat.DamageStunPercent` the flinch chance; `Ai.SightRadius` the notice
  radius; `Ai.Iq` enables running; `Ai.GroupSize` the spawn group. `Ai.MoveSpeed` is only sent to clients for
  presentation; `Ai.MoveRange`, `Ai.Nature` and `Ai.ActiveHour` are imported but no rule reads them.
- `SoundCode` is the original sound family code and doubles as a behaviour key (see Pitfalls).
- `MonsterCatalog.TryFind(name)` matches the display name case-insensitively and returns the lowest code.

`MonsterInstance` holds live state: `Position`, `Facing`, `Life`, `AiState`, `Motion`, `HomeAnchor` (spawn position),
`SpawnPointIndex` (-1 for summons), `TargetId`, timers, manoeuvre counters, `Effects` (modifier container stepped by the
field) and summon fields (`OwnerId`, `ExpiresAtMs`, `Aggressive`, `ModelScale`). `ApplyDamage` clamps at 0 and enters
`Dead`. `PresentedMotion` reports `Stand` instead of walk/run on ticks where the host did not actually move it.

## Monster AI

`MonsterAiBrain.Tick(monster, tuning, prey, dtMs)` mutates the instance and returns a `MonsterAttackEvent` when a swing
or skill lands. The host then re-resolves the position the brain wrote (see Movement) and resolves the hit.
`MonsterAiTuning` is built per definition with its `MotionTimingSet` and cached per field by monster code.

States (`MonsterAiState`): `Idle`, `Wander`, `Chase`, `Leash` (a fixed-heading manoeuvre), `Attack`, `Dead`.

| Constant | Value | Meaning |
|---|---|---|
| `DecisionIntervalMs` | 60 | Decision cadence; between decisions the monster keeps moving |
| `RetargetIntervalMs` | 900 | Target scan cadence (suspended only while a boss target lock runs) |
| `VerticalAggroGate` | 140 | Prey this far above or below is ignored |
| `RunDistance`, `RunIqThreshold` | 512, 6 | Runs if `Iq >= 6`, not engaged and target closer than 512 |
| `StopMargin`, `ChaseHysteresis` | 8, 24 | Approach to `MeleeRange - 8`; engaged flag clears beyond reach + 24 |
| `DefaultLeashRadius` | 1280 | Planar distance from `HomeAnchor` that starts a leash manoeuvre |
| `ProgressProbeClips`, `StuckDistance` | 5, 32 | Every 5 locomotion clips, less than 32 units of progress means stuck |
| `LeashManoeuvreClips`, `StuckManoeuvreClips` | 6, 10 | Manoeuvre lengths in locomotion clips |
| `BossTargetLockMs` | 4000 | Boss keeps a new attacker as target this long |
| `FlinchFallbackMs`, `LocomotionCycleFallbackMs` | 700, 1000 | Used when no clip was imported |

Behaviour, in tick order:

1. Stun or charm: stand still; an attack in progress is cancelled without landing. Slow: `dtMs` is scaled by
   `(100 + MoveSpeed%) / 100`, clamped 0.05-1, so movement, decisions and swings all slow down.
2. Flinch (`Damage` motion) blocks everything until `FlinchMsLeft` drains, then chase (target alive) or idle.
3. Attack state advances the swing (see below).
4. Target scan: nearest live prey by 3D distance within sight (scaled by the prey's `SightPercent` for stealth unless
   the monster is one of the four unyielding bosses), skipping prey beyond the vertical gate.
5. Clip bookkeeping for `Chase`/`Leash`: the progress probe can start a stuck manoeuvre (turn toward home ±45 degrees,
   run if able, 10 clips); a moving monster beyond the leash radius starts a leash manoeuvre (head home, 6 clips). Both
   keep the target and end in chase.
6. Decision: no target means idle; a new spawn walks forward along its spawn facing for two walk clips, then stands
   (idle monsters do not wander again). With a target: attack if `MonsterAttackReach.CanBegin` (planar distance below
   `MeleeRange`, height difference at most 64), else walk or run to a point `StopDistance` short of the target.
   A target in reach is attacked even during a manoeuvre.

Attacks: on entry the skill roll is `rand(100) < SkillRatingPercent` if the definition has a supported offensive skill
and a skill clip. The hit fires at the windup time: a skill always fires (area placed at cast; the host filters
victims); a basic attack lands only if the target is alive and `MonsterAttackReach.CanLandBasic` (vertical at most 64,
planar distance squared below 48×256, or below 460² for ranged sound families). At clip end the monster swings again
at once if the target is still in reach, else chases or idles.

Timing: with an imported attack clip, cooldown is the clip duration at rate 1 and windup its first event; without one,
`rate = 256 + 32 * clamp(AttackSpeed - 6, 0, 6)`, cooldown `1000 * 256 / rate` ms, windup `cooldown * 40 / 72`.
Flinch duration is the damage clip at `DamageRateFactor`; walk and run cycles come from their clips.

Host-called rules:

- `ApplyFlinchOnHit`: needs `damage * k >= MaxLife` (`k` 5 above level 14, else 10) or a critical, then
  `rand(100) < DamageStunPercent`; cancels a swing in progress.
- `ApplyBossRetargetOnHit`: a boss without an active lock takes the attacker as target for 4000 ms.
- `MonsterAiTuning.IsUnyielding`: the four great bosses by sound code; skills never knock back or taunt them and they
  see through stealth.
- A definition with `MeleeRange == 0` is immobile and never attacks (`IsImmobile`).

Speed (`MonsterLocomotion`): 70 units/s per step; a Dark Knight boss has 2 base steps; running adds one step (Greven 4,
Wolverlin 2). The brain integrates toward its target at this speed; terrain then accepts or slides the step.

### Summons

`SummonBrain.Tick(summon, tuning, orders, dtMs)` takes `SummonOrders` (owner position, target) from the skills layer.
It runs back to the owner (dropping the target) beyond 300 units with a target or 150 without, stops at 90; attacks
when within `max(80, MeleeRange + 32)` planar and 64 vertical; uses the same clip timing. `NeedsRecall` (500 units)
tells the host to place it beside the owner. Only stun stops it. Wild monsters see summons as target-only prey (they
fight a summon that hits them, never pick it by sight). Summon skills are documented in [skills.md](skills.md).

## Spawning

`FieldSpawnData` per field code comes from the database (`WorldDataFactory.ToFieldSpawnData`, imported from the field
spawn files; see [world-import.md](world-import.md)): `SpawnPoints` (X/Z, `Active`), weighted `SpawnTable`,
`BossSpawns` (boss, escort, escort count, hours), `FixedNpcs`, and knobs `MaxConcurrentMonsters` (10),
`MaxPerSpawnPoint` (3), `SpawnIntervalMask`, `PerPointCooldownMs`, `SpawnPassIntervalMs` (1800), `IdleDespawnMs`
(15000), `BossIdleDespawnFactor` (32). Unresolved monster or NPC names are dropped at load.

`FieldSpawner.MaintainPopulation`, once per pass:

1. Pass timing: every `SpawnPassIntervalMs`, or with `SpawnIntervalMask` set, `SpawnCadence` on a 70 Hz frame clock
   fires on the next frame number with no bit in the mask; missed opportunities after a hitch are skipped.
2. Stop if wild live monsters (summons excluded) reach `MaxConcurrentMonsters`.
3. Pick one definition by weight (half-open cumulative roll).
4. Pick the next spawn point round-robin that is active, below `MaxPerSpawnPoint`, off cooldown and has a player near.
5. Group size `rand[GroupMin, GroupMax]` (at least 1), capped by field and point room. Leader on the point, others
   within 64 units, random facings. The host places each on the floor. The point's cooldown starts.

`MaintainDespawns`: a live wild monster with no player near loses `dtMs` from `IdleDespawnMsLeft` (bosses get
`IdleDespawnMs * 32`); a player near resets it; expired monsters are returned for removal.

Bosses: `BossSpawnSchedule` (one per server) picks a random minute 1-45 each local day and opens a pass at that minute
of every hour, at most once per hour slot and never within 10 minutes of the last pass. `WorldServer` hands the hour to
every live field; `SpawnScheduledBosses(hour)` spawns each `BossSpawn` whose hours contain it at the first active
point with a player near (else the first active point), plus escorts scattered within 64 units.

## Loot and kill rewards

`DropTable` entries are `Nothing`, `Gold` (min-max) or `Item` (runtime item code), each with a weight and `IsBonus`.
Built by `WorldDataFactory.ToDropTable`: unresolved main drops stay as `Nothing` slots so probabilities keep their
shape; unresolved bonus drops are omitted.

`DropRoller.Roll(rng, table, hasEventItem, eventItemCode, extraRollChance, eventRoll)`:

1. Optional event item first, flagged `ToInventory` (ground loot rejects it; it needs its own reward transaction).
2. Each bonus entry rolls independently: `rand(10000) < Weight`.
3. `MaxItemsPerKill` weighted picks from the main entries (`Nothing` picks add nothing). Gold amount is
   `rand[GoldMin, GoldMax]` inclusive. A running item event (`eventRoll`, `ItemEventDrops`) may replace each pick with
   its item; a puzzle piece is flagged `Sealed` ([special-items.md](special-items.md)).

Quest and ingredient items (`QuestItems.IsQuestDrop`) are flagged `ToInventory` with `FromMainTable` set for main-table
picks; the field hands them to the credited player's quest upkeep instead of the ground ([quests.md](quests.md)).

`KillReward.ExpForKill(baseExp, monsterLevel, playerLevel, multiplier)`: `baseExp * multiplier`, then 100 / 85 / 70 /
55 percent for a level gap of at most 10 / 20 / 30 / more. The server passes `World:ExperienceMultiplier`.
`FirstAttackerStealsCredit`: the first attacker gets the kill over another killer if it dealt at least 40% of max life
and hit within the last 15 s (`FirstAttackerDisengageMs`).

Drops settle on the floor within 32 units of the corpse. `DropTable.AllPlayersSeeDrops` makes a drop public at once;
lifetimes and the owner window live in `FieldGroundLoot` ([application.md](application.md)). `GroundDropHandle` maps
the 32-bit wire handle to a GUID whose other 12 bytes are zero.

## Motion timing

INX motion tables are extracted by the importer (`MotionTableExtractor`) into the database and loaded as a
`MotionTimingCatalog` keyed by `NormalizeKey(modelPath)`: extension `.inx`, forward slashes, lower case.

- `MotionClip`: `StartFrame`, `EndFrame`, four event offsets in ticks, `Repeat`. `Frames = EndFrame - StartFrame`.
  `TicksPerFrame = 160`; an event is valid only if `0 < ticks <= Frames * 160`.
- `MotionTiming.DurationMs(frames, rate) = frames * 33 / rate`;
  `EventOffsetMs(ticks, rate) = ticks * 33 / (160 * rate)`.
  `DamageRateFactor = 92 / 80` is used for flinch clips and the potion (`Eat`) clip.
- `MotionStateCode`: Stand 0x40, Walk 0x50, Run 0x60, Attack 0x100, Damage 0x110, Dead 0x120, Eat 0x140, Skill 0x150,
  Restart 0x200.
- `MotionTimingSet.TryGetClip(state)` returns the first clip of the state (the one the client plays);
  `ClipsForState` returns all (NPC stand variants). Zero-length clips are dropped.

Player action timing (`FieldInstance.PlayerActionTiming`): start from `PlayerMotionFallback.GetClip(class, state)`,
replace with the job body's catalog clip, and for attacks replace with `PlayerEquipmentTiming.TryGet(job, weaponKey)`.
Attack rate is `Character.AttackRate`. The hit lands at event 0, or at the clip end if there is none.

`PlayerEquipmentTiming.cs` is generated from the shipped INX files by the test runner
(`--export-equipment-timings <assetsRoot> <outputFile>`; `--verify-equipment <assetsRoot>` checks it).

## NPCs and services

`NpcDefinition` comes from `WorldDataFactory.ToNpcDefinitions`: `Code` is the 1-based record position, `Name` the
authored identity that field placements reference, `DisplayName` the player label (`Label` falls back to `Name`),
`Services` in authored order with a bit set `ServiceSet` (`1 << kind`), `Shop` stock in three tabs (weapons, armour,
misc), `Dialog`, `MoveRange` (default 64). A shop service without runtime stock is removed. Databases without typed
service rows fall back to the older per-service flag columns.

NPC placement and wandering (`WorldNpc`, Application): placed from `FixedNpc` (fixed point, 4096-step angle); walks
only with `MoveRange > 0` and a walk clip, with a 25% chance at the end of each stand clip, at 70 units/s.

| Type | Contract |
|---|---|
| `NpcServiceKind` | Stored in `npc_services.kind`, sent in the dialog and as bit `1 << kind`; append only. Argument meanings per member. |
| `NpcServiceAvailability.IsAvailable` | Running services: `Shop`, `Warehouse`, `SkillMaster`, `Teleporter` with a known route. Others are offered but unavailable. |
| `NpcInteraction` | Dialog reply; `Reach = 96` planar units for NPC service requests |
| `NpcServiceCommand` | `OperationId` (idempotency), NPC, service, action, expected revisions, item, `Anchor`, `Count`, `Amount` |
| `NpcServiceAction` | Persisted in the operation history and sent on the wire; append only. `IsDurable` is false for Open, Close, Refresh. |
| `NpcServiceStatus` | Outcome codes on the wire (`Ok` ... `NotOpen`, `OverWeight`); append only |
| `NpcServiceReply` | Outcome plus the view after it: shop catalog, quotes, warehouse, teleport destinations, arrival |

Shop rules (`NpcShopRules`):

- Buy: catalog price times count (count 1, or 1 to `MaxStack` for stackables); needs gold, a free cell and room under
  the weight limit (`OverWeight`; a potion weighs one per unit). The bought copy is the shop form of the item
  (`ItemRoller.Lower`, see [simulation.md](simulation.md#items)).
- Worn value: `intact = price * dur / maxDur` (0 counts as 1), `worn = intact + (price - intact) / 4`, where price is
  the copy's own price (a specialized drop costs a fifth more than its kind).
- Sell: `min(price / 5, worn / 5)`; stackables `(price / 5) * count`; quest items and the quest weapon and ring a
  running rank-up holds cannot be sold; refused with `GoldLimit` when gold would exceed
  `MoneyLimits.CarryLimit(level, tier)`.
- Repair: `(price - worn) / 8` for items below max durability; requires gold strictly greater than the cost.
- Carry limit: 200,000 up to level 10; tier 0 `max(200,000, level * 200,000 - 1,800,000)`; tier 1 10M; tier 2 50M;
  higher 100M.

Warehouse (`WarehouseStorage`, one per account): 9×9 grid, at most 100 items, weight 1000, gold 100,000,000; quest
items, materials and the quest weapon and ring a running rank-up holds are refused. Every change increments `Revision`;
`Clone` supports prepare-then-commit. A withdrawal that would carry the character over its weight limit is refused
(`OverWeight`), and an overweight character cannot open the warehouse (`NpcServiceTransactions.Check`).

Teleport routes (`TeleportRoutes`): route 0 means route 1. Route 1: index 1 field 0 "Mushroom Cave" 100 gold, 2 field 18
"Beehive Cave" 100, 3 field 7 "Cursed Land" 500, 4 field 12 "Forbidden Land" 1000. The destination field's
`LimitLevel` applies; arrival is its first start point (or `Center`) placed on the floor. The cave destinations are the
fields that hold the cave entrances: field 0 (`fore-3`) has the warp gate to `Tcave`, field 18 (`forever-fall-03`) the
one to `Mcave`.

`LegacyNpcSoundCode.Resolve(key)` maps the authored sound keyword of a monster or NPC to its sound family code. The
importer stores it; the client audio library and the AI rules key on it.

## Driving surface (Application)

- `WorldServer` tick (20 Hz): `BossSpawnSchedule.TryBeginPass`, then every live `FieldInstance` steps in parallel.
  Field changes go through `FieldTransition.TryResolve`; entry, respawn and teleport use `FieldTerrain.PlaceOnFloor`
  or `SettleNear`.
- `FieldInstance.Step` order: rebuild prey, `FieldSpawner.MaintainPopulation`, `MaintainDespawns`,
  `SpawnScheduledBosses`, rebuild the `SpatialIndex`, `MonsterAiBrain.Tick` then `MonsterStepping.Step` per wild
  monster, summons, NPC steps, players, skills, corpses, ground loot.
- `FieldInstance` implements `ISpawnSink` (creates the `MonsterInstance` on the floor), `IPlayerProximity`,
  `IStepOccupancy` (monster occupancy) and the prey view (`IPreyView`).
- `FieldCombat`: `ApplyFlinchOnHit`, `ApplyBossRetargetOnHit`, `MonsterAttackReach.CanLandBasic`, `KillReward`,
  `DropRoller.Roll`.
- `WorldPlayer.ReportMovement`: `FieldTerrain.CrossesWall`, `Slide`, `TryFloorNear`.
- `NpcServiceTransactions`: `NpcShopRules`, `MoneyLimits`, `WarehouseStorage`, `TeleportRoutes`,
  `NpcServiceAvailability`.

Each `FieldInstance` owns a `CombatRng` seeded with its field code (shared by brain, spawner and drops) and is created
on first use. Fields share only immutable catalogs, terrain and the id allocator, which is what makes the parallel step
safe. Rules here run inside the tick and must not block: no I/O, no lock waits, no tasks.

## How to

- **Add a field**: create a static class with `Definition` in the region file under `World/Fields/` (unique `Id` =
  stage base name, unique `FieldCode`), add it to the `WorldCatalog()` list, add gates on both sides and warp gates
  with destinations. Re-run the importer so spawn files join by the stage base name; the server will refuse to start
  without the stage file. Check `tests/PristonTale.Tests/FieldAudioTests.cs` and `DomainTests.cs`.
- **Tune monster AI**: edit `MonsterAiTuning` constants or `MonsterAiBrain`; keep decisions on `ISimRng` for
  determinism. Run `MonsterMovementTests`, `MonsterNavigationTests`, `MonsterCombatTests`, `MonsterSkillTests`. Clients
  receive each swing's duration and hit time in `SwingBegin` combat events.
- **Change terrain rules**: `FieldTerrain`/`MonsterStepping` and `TerrainTests`. If the stage subset changes, keep
  `StageCollisionReader` and the client SMD reader in parity (`TerrainTests` compares them).
- **Change spawning**: `FieldSpawner`/`SpawnCadence`/`BossSpawnSchedule`; data knobs need the importer
  (`tools/PristonTale.WorldImport/Parsers/SpmParser.cs`), the persistence records and
  `WorldDataFactory.ToFieldSpawnData`.
  Run `SpawnGateTests` and `SpawnScheduleTests`.
- **Change drops or experience**: `DropRoller`, `DropTable`, `KillReward`, `WorldDataFactory.ToDropTable`; run
  `WorldRewardsTests` (and the `--verify-world-rewards` probe against a real database).
- **Add an NPC service**: append to `NpcServiceKind` (and `NpcServiceAction` if needed), map the authored key in
  `tools/PristonTale.WorldImport/Parsers/NpcParser.cs`, enable it in `NpcServiceAvailability`, implement it in
  `Shared/PristonTale.Application/World/NpcServiceTransactions.cs`, map it in the protocol and client dialog, add
  `NpcServiceTests`. Update [application.md](application.md), [protocol.md](protocol.md),
  [world-import.md](world-import.md) and the README section on NPC services.
- **Add a teleport route**: add the route to `TeleportRoutes`; availability follows. Update `NpcServiceTests` and the
  README teleporter list.
- **Regenerate player attack timings**: run the test runner with `--export-equipment-timings <assetsRoot>
  Shared/PristonTale.Simulation/World/Motion/PlayerEquipmentTiming.cs`, then `--verify-equipment <assetsRoot>`.

## Pitfalls

- `SoundCode` selects behaviour: ranged basic reach (`MonsterAttackReach.HasRangedBasicAttack`), supported offensive
  skills (`MonsterOffensiveSkill`), unyielding bosses, locomotion speed. Changing a sound mapping changes combat.
- Monster and NPC codes are load-order positions, not persistent ids. Persist names, never codes.
- `PlaceOnFloor` takes the highest floor anywhere under a point, which can be a roof or bridge; use `SettleNear` or
  `TryFloorNear` when a reference height exists.
- Basic hit reach compares squared distance with `48 * 256` (about 111 units). This is intentional; do not turn it
  into a radius of 48.
- Entity pair distance is the smaller radius, not the sum. Monsters ignore players and NPCs when stepping.
- `FieldDef.Spawns` is never read by the server; spawn data lives in `WorldData.FieldSpawns` keyed by field code.
- Warp gates with `SpecialEffect == 2` never authorize a transition.
- `FieldTerrain.None` makes every move legal; a server started with `Assets:AllowMissingTerrain` has no wall checks.
- `MonsterAiTuning` is cached per field by monster code; a definition altered with `with` for a summon shares the
  original code's tuning.
- `Idle` monsters do not wander after the spawn walk; they only react to prey or damage.
- Do not hand-edit `PlayerEquipmentTiming.cs`; regenerate it.
- Appending is the only safe change to `NpcServiceKind`, `NpcServiceAction` and `NpcServiceStatus`: values are stored
  and sent on the wire.

## Related

- [architecture.md](architecture.md) - layering, tick and threading model
- [application.md](application.md) - `WorldServer`, `FieldInstance`, combat, ground loot, NPC transactions
- [simulation.md](simulation.md) - characters, items, combat formulas, modifiers
- [skills.md](skills.md) - skills, summons and monster-affecting effects
- [world-import.md](world-import.md) - importer and authored data contracts (spawn files, NPC files, INX extraction)
- [persistence.md](persistence.md) - world data schema and `WorldDataFactory`
- [server.md](server.md) - assets root and terrain loading configuration
- [rendering.md](rendering.md) - client SMD reader and stage presentation
- [testing.md](testing.md) - regression runner and probes
- [glossary.md](glossary.md)
