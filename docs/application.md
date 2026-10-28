# Application layer and authoritative world

`Shared/PristonTale.Application`: transport-free use cases (auth, characters, sessions, NPC services, quests), the
session registry, and `WorldServer`, the single owner of live world state with its 20 Hz tick. The TCP edge that calls
into it is described in [server.md](server.md); skill resolution details are in [skills.md](skills.md); quests in
[quests.md](quests.md).

## Read this when

- You add or change a player command that mutates world state (movement, attack, cast, field change).
- You add an NPC service, a shop/warehouse/teleporter action, or change prices, reach or revision checks.
- You add a durable character operation (inventory, progression, pickup) or touch the prepare/persist/commit flow.
- You debug a `WorldBusyException`, a "save or character operation pending" reply, or an operation id conflict.
- You change what the server publishes per tick (entities, combat/skill/status events, cooldowns, ground drops).
- You touch kill credit, experience, drops, corpses, respawn, regeneration or summon ownership on the server.
- You need to know what may run on the tick thread and what must stay off it.

## Code map

| Path | Responsibility |
| --- | --- |
| `Services/AuthAppService.cs` | Login against `IAccountRepository`, creates a `PlayerSession` with the account's `GmLevel` |
| `Services/CharacterAppService.cs` | List, name check, create, delete characters; builds `CharacterSummary` for the select screen |
| [`Services/SessionAppService.cs`](../Shared/PristonTale.Application/Services/SessionAppService.cs) | World entry, saves, durable inventory and progression operations (`ApplyDurableAsync`) |
| [`Services/NpcAppService.cs`](../Shared/PristonTale.Application/Services/NpcAppService.cs) | NPC click and NPC service requests; warehouse load and joint character+warehouse write |
| [`Services/QuestAppService.cs`](../Shared/PristonTale.Application/Services/QuestAppService.cs) | Quest requests and the quest upkeep (see [quests.md](quests.md)) |
| `Services/ItemUseAppService.cs` | Use of a carried item: prepare, store, commit ([special-items.md](special-items.md)) |
| `Services/PersonalShopAppService.cs`, `World/WorldServer.PersonalShops.cs`, `World/PersonalShopTransactions.cs` | Personal shops: open, close, view, and a purchase that holds and stores two characters (below) |
| `Chat/`, `GameMasters/` | Chat routing, whispers and notices; game-master commands, levels and audit ([chat-and-gm.md](chat-and-gm.md)) |
| `World/WorldServer.Chat.cs`, `World/WorldServer.GameMasters.cs` | Players in hearing range; tick-side game-master reads and changes (kill, hide, move, spawn, drop, gifts) |
| `Shared/PristonTale.Contracts/Sessions/*Outcome.cs`, `CharacterSummary.cs`; `Contracts/World/Restart.cs`, `WingGateTrip.cs` | Result records returned to the edge and read by the client (login, characters, world entry, restart, wing trips); failure enums carry stable codes |
| `Sessions/` | `PlayerSession` (state machine, `SaveGate`, loaded record, open warehouse), `SessionRegistry` |
| [`World/WorldServer.cs`](../Shared/PristonTale.Application/World/WorldServer.cs) | Command queue, tick, field registry, connections, prepare/commit entry points |
| `World/WorldServer.Npc.cs` | NPC interaction, NPC service check/prepare/commit, teleport arrival |
| `World/WorldServer.Quests.cs`, `World/FieldInstance.Quests.cs`, `World/QuestTransactions.cs`, `World/PlayerQuestState.cs` | Quest prepare/commit, upkeep requests, quest travel; kill credit, quest drops, timers and arena duels on the field |
| `World/WorldServerOptions.cs` | `ExperienceMultiplier`, `ExperienceEventPercent`, `ClockOffsetMinutes`, `CrystalEvent`, `ItemEvents`, `Aging`, `MaxPendingCommandsPerPlayer`; `WorldBusyException` |
| `Settings/` | `ServerSettings`, `ServerSettingCatalog` (every row of the settings table), `ServerSettingsAppService` (read, apply, write) |
| [`World/FieldInstance.cs`](../Shared/PristonTale.Application/World/FieldInstance.cs) | One live field: players, monsters, NPCs, spawner, AI, collision index, step order, views |
| `World/FieldCombat.cs` | Basic attack and monster attack resolution, damage log, kill payout, corpses |
| `World/FieldMonsterSkills.cs`, `FieldInstance.MonsterSkills.cs`, `FieldSkills.MonsterStatuses.cs` | Monster skill authority: swing start rules and events, skill blows, statuses, curses, boss lives and reinforcements, potions, shape changes (see [world.md](world.md#monster-skills)) |
| `World/FieldSkills*.cs` | Player skill authority, effect timers, regeneration, familiars, summons (see [skills.md](skills.md)) |
| `World/WorldPlayer.cs` | Server-side avatar: movement validation, action state (swing, flinch, eat, cast, cheer), death, respawn |
| `World/ActionTimeline.cs` | The running action's clip (elapsed, length, one event that fires once); every action of `WorldPlayer` runs on it |
| `World/WorldNpc.cs` | Placed NPC with deterministic wander |
| `World/FieldGroundLoot.cs` | Ground drops per field: ownership window, expiry, reservation for pickup, client handles |
| `World/FieldTransition.cs` | Authorizes gate and warp field changes |
| `World/*Transactions.cs`, `World/Prepared*Operation.cs` | Prepare side of durable operations (pickup, progression, NPC services, quests); `PreparedOperation` is the base |
| `Services/DurableOperations.cs` | `DurableOperations`, `DurableLease`, `DurableFailure`: the admit, prepare, store, commit and abort runner every durable service uses |
| `World/WorldSnapshot.cs` | Published view types: `EntityView`, `CombatEventView`, `SkillEventView`, `StatusEventView`, `WorldSnapshot` |
| `World/WorldOutbox.cs` | Per-connection coalescing channel between the tick and the edge; pins frames on read, keeps events per tick |
| `World/SubscriberPresence.cs` | Spawn/state/remove diff of snapshots per subscriber (used by tests; the server encoder diffs itself) |
| `World/PresenceFrame.cs` | `PresenceFrame` (one field tick's views, grid and event routes, reused across ticks, pinned by readers), `FieldPresenceFrames`, `ViewArrays` |
| `World/EventRoutes.cs` | The tick's combat, skill, status and item events sorted by the entity ids they name |
| `World/EntityGrid.cs` | Sorted 64-unit planar buckets, square counts, who-targets-or-owns-whom and the NPC list, rebuilt once per frame |
| `World/FieldDescriptors.cs` | Field-wide descriptor revision of every entity, computed while the frame's views are filled |
| `World/FieldStatusBook.cs` | The statuses running on the field's entities, for viewers that start seeing an entity later |
| `World/WorldServer.Movement.cs` | Movement reports queued as values (`TryReportMovement`) with an `IMovementVerdictSink` |
| `World/PlayerSkillCooldowns.cs` | Cooldowns on the field clock, rebased on field change, revisioned for snapshots |
| `World/WorldData*.cs`, `FieldTerrainCatalog.cs` | Catalogs loaded at startup (`WorldDataAppService.BuildAsync`), held in volatile holders |
| `World/WorldStatusReadModel.cs`, `ServerStatus.cs` | Lock-free player and per-field counts for status reporting |
| `World/EntityIdAllocator.cs` | Thread-safe, monotonic 64-bit monster/NPC/summon ids from `2^32` |

The project references only `PristonTale.Simulation` and `PristonTale.Persistence.Abstractions`, and exposes internals
to `PristonTale.Tests`. No sockets, packets, SQL or MonoGame.

## Use cases (transport-free services)

| Service and method | Contract |
| --- | --- |
| `AuthAppService.LoginAsync` | Empty input, unknown account, wrong password: `BadCredentials` (-2); blocked: `LoginDenied` (-24). Values are wire codes. Success returns a token. |
| `CharacterAppService.ListCharactersAsync` | Summaries with armour/weapon/off-hand catalog keys and `InVillage`; throws `InvalidDataException` on corrupt saved equipment |
| `CharacterAppService.CreateCharacterAsync` | Trimmed name 3-16, defined class, valid face, unique name, `CharacterSlots.Maximum` per account, first free slot, starter record in field 3 |
| `CharacterAppService.DeleteCharacterAsync` / `NameExistsAsync` | Account-scoped delete by name; global name check. |
| `SessionAppService.EnterWorldAsync` | Load record, validate field and class, claim the character, `CharacterStateCodec.Restore`, clamp resources, resolve spawn, mark ready |
| `SessionAppService.SaveCharacterAsync` | Waits for `SaveGate`; captures on the tick while playing, else from the session; writes through `ICharacterRepository.UpdateAsync`. |
| `SessionAppService.ApplyInventoryAsync` / `ApplyProgressionAsync` | Durable operations, see [Durable operations](#durable-operations). |
| `NpcAppService.InteractAsync` / `RequestAsync` | NPC dialog and services, see [NPC services](#npc-services). |
| `QuestAppService.RequestAsync` / `RunUpkeepAsync` | Quest steps at an NPC and the quest upkeep the tick asks for, see [quests.md](quests.md#server-authority). |
| `WorldDataAppService.BuildAsync` | Reads world-data repositories into `WorldData` (items, monsters, NPCs, field spawns, motion timings). |

`CharacterAppService` throws `InvalidSessionException` for an unknown token; the other services return a rejected
outcome. Failure enums (`CreateCharacterFailure`, `EnterWorldFailure`, `LoginFailure`) have a fixed `MessageFor` text.

## Sessions and ownership

`SessionRegistry` (singleton) maps a GUID token (`"N"` format) to a `PlayerSession`. The player entity id is assigned
at login from an interlocked 64-bit counter starting at 1; monsters, NPCs and summons use `EntityIdAllocator`, a
monotonic 64-bit counter from `2^32`, so the two ranges never collide and no id is ever reused. Entity ids are `ulong`
everywhere: in the world, the views, the wire and the client.

`PlayerSession` state (an int changed only by compare-exchange):

| State | Entered by | Left by |
| --- | --- | --- |
| 0 authenticated | `Create` | `TryBeginEntry` (to 1) |
| 1 loading | `EnterWorldAsync` | `CompleteEntry(true)` to 2, `CompleteEntry(false)` back to 0 |
| 2 ready | successful entry | `TryBeginPlay` (to 3), called by the edge before `JoinAsync` |
| 3 playing (`IsPlaying`) | edge | `EndPlay` (to 4) after `LeaveAsync` |
| 4 closing | `EndPlay` or `SessionRegistry.Remove` (`MarkClosed`) | terminal; a session enters the world once |

Character ownership: `TryClaimCharacter(characterId, token)` admits one session per character
(`EnterWorldFailure.AlreadyActive` otherwise). The claim is released when entry fails or the session is removed.
`EnterWorldAsync` re-checks that the token still maps to the same session before publishing the loaded state.

Spawn resolution on entry: saved X/Z when valid and not both zero, else the field's first start point, else its
center. Height is resolved later on the field terrain (`FieldTerrain.PlaceOnFloor` in `JoinAsync`).

`SaveGate` (`SemaphoreSlim(1,1)`) serializes everything that writes the character: saves wait for it; inventory,
progression and NPC operations try it with zero timeout and answer "save or character operation pending; retry" /
`NpcServiceStatus.Busy`. `session.Warehouse` (the account warehouse while open) changes only under the gate.

Lifecycle seen from the edge: `LoginAsync` -> `EnterWorldAsync` -> `TryBeginPlay` -> `WorldPlayerFactory.Create` ->
`WorldServer.JoinAsync(field, player, outbox.Writer, session)` -> play commands -> `LeaveAsync` -> `EndPlay` ->
`SaveLeftCharacterAsync` (the capture `LeaveAsync` took) -> `SessionRegistry.Remove`. While a durable operation holds
the session's `SaveGate`, `LeaveAsync` takes no capture (it only records the placement): that operation commits after
the leaving tick, possibly on another character too (a personal shop sale), so the save waits for the gate and captures
the character the operation leaves (`SaveCharacterAsync`); a player that left dead (`PlayerSession.LeftDead`) is then
captured through its town restart (`WorldServer.CaptureLeftAsync`).

## Authoritative world

### Tick loop

`WorldTickService` (server) calls `WorldServer.Tick(1/20 s, ++tick)` from a `PeriodicTimer` at `WorldServer.TickHz`
(20). `Tick` ignores non-finite or non-positive `dt`, then:

1. Command phase: drains up to `CommandsPerTick` (16384) queued commands on the calling thread.
2. Boss clock: `BossSpawnSchedule.TryBeginPass` on the injected `TimeProvider` yields at most one hour value per pass,
   shared by all fields.
3. Field phase: `Parallel.ForEach` over every created field: `FieldInstance.Step` (which also updates the status book),
   then (only with players present) rent a `PresenceFrame`, fill its views with their descriptor revisions, rebuild its
   grid, open it to readers, build one `WorldSnapshot` over it, index the snapshot's events in the frame's
   `EventRoutes`, and write the snapshot, stamped per player with `OwnerState`, changed
   cooldowns and the quest update, to each connection's outbox.
4. When membership changed: publish `PlayerCount` and per-field counts with `Volatile.Write` for the read model.

### Command queue

All world access from other threads goes through `EnqueueAsync`, which wraps a closure into a bounded
(65536, single reader) channel and returns a `Task` completed on the tick thread. A closure that throws faults the task.
Movement reports, the bulk of the traffic, use `TryReportMovement`: the report travels in the same ordered queue as a
value (no closure, task or completion), counts against the same quota, and its verdict is handed to the caller's
`IMovementVerdictSink` on the tick thread; `false` means quota or queue full and the report is dropped.

- Per-player quota: commands tagged with the player's entity id count against `MaxPendingCommandsPerPlayer`
  (default 64, config `World:MaxPendingCommandsPerPlayer`). Over quota, or with the channel full, `EnqueueAsync`
  throws `WorldBusyException` synchronously; the edge drops the command like a lost packet.
- Exempt commands use owner 0 (`NoOwner`) and `waitForCapacity: true`: `LeaveAsync`, `CaptureForSaveAsync`, every
  `Commit*Async` and `AbortCharacterOperationAsync`. These cannot be refused because a durable write may already
  have happened or the player is leaving.
- The per-player counter entry is removed on leave. `PendingCommandsOf(entityId)` exposes it for tests.

### Threading model

| Context | May touch | Must not |
| --- | --- | --- |
| Caller threads (edge, services) | Session fields under `SaveGate`, repositories, `EnqueueAsync` | Any `WorldServer`/`FieldInstance`/`WorldPlayer` state |
| Command phase (tick thread, serial) | Everything in `WorldServer`: `_fields`, `_connections`, players, fields | Await, block, do IO, wait on locks |
| Field phase (parallel per field) | Its own field, its players' connections (outbox write, owner/cooldown stamps), immutable catalogs, `EntityIdAllocator` | Other fields, `_fields`/`_connections` mutation, IO |

Immutable shared inputs: `WorldDataHolder.Current` and `FieldTerrainCatalog` are swapped whole (volatile) at startup
by the server's load services before players join. Tick-owned scratch lists in `FieldInstance` and `FieldSkills` avoid
per-tick allocation; event lists are copied to arrays before publication. Presence frames are the only state shared
with the edge's threads: a frame is written only while no reader pins it and it is not the field's latest published
frame, so a reader that pinned a generation sees it unchanged until it releases it.

Persistence never runs on the tick. Durable work is split: the tick prepares a candidate and a
`CharacterSaveSnapshot` (pure memory), the service writes it off the tick, the tick commits. See below.

### Interface used by the TCP edge

| Call | Queue owner | Result |
| --- | --- | --- |
| `JoinAsync(fieldId, player, outbound, session)` | player | false if unknown field, already connected, or session not playing |
| `LeaveAsync(fieldId, entityId, outbound)` | exempt | removes the player only if `outbound` matches the connection; returns `WorldLeave` with the character captured in that tick, as `CaptureForSaveAsync` captures it |
| `MoveToFieldAsync(old, new, player, requestedPosition, outbound)` | player | gate or warp change authorized by `FieldTransition`; true without moving again when the connection is already in `new` (reports sent before the move committed) |
| `TryReportMovement(field, entity, position, facing, moving, running, actionTarget, sink)` | player | false when dropped; the verdict goes to `sink` on the tick |
| `ReportMovementAsync(field, entity, position, facing, moving, running, actionTarget)` | player | `MovementVerdict` (tests and tools) |
| `SetAttackTargetAsync(field, attacker, target, sustained, sequence)` | player | false on dead attacker, stale sequence, invalid target |
| `CastSkillAsync(field, entity, SkillCastRequest)` | player | `SkillCastOutcome` (cast, release, summon mode) |
| `InteractNpcAsync(session, npcId)` | player | `NpcInteraction` (offers + one random dialog line) |
| `PositionOfAsync(entity)` | player | current position or null |
| `PlayerCount`, `ActiveFieldCounts()` | none (volatile) | read model values |

Durable entry points (`Prepare*Async`, `Commit*Async`, `CheckNpcServiceAsync`, `CaptureForSaveAsync`) are called by
`SessionAppService` and `NpcAppService`, not by the edge directly.

## Field instances

- Created lazily by `GetField` on first join or arrival, never removed, and stepped every tick even when empty
  (population, despawns, boss passes). Snapshots are built only when the field has players.
- Each field owns a `CombatRng` seeded with its field id, its own clock `NowMs` (starts at 0 when created) and its
  stage collision from `FieldTerrainCatalog` (`FieldTerrain.None` when no stage was loaded).
- Fixed NPCs are placed in the constructor; a placement that references a missing NPC throws `InvalidDataException`.

`FieldInstance.Step(dt, bossHour)` order:

1. Advance the clock, clear events, `FieldSkills.BeginTick` (moves cast start events queued by commands into this
   tick), rebuild the prey view (players; live summons as target-only).
2. Spawner: maintain population and despawns for wild monsters; scheduled bosses when the hour pass is due.
3. Rebuild the collision index; step wild monsters (AI brain, swing-begin events, `MoveMonster`, hit resolution).
4. Step summons, then NPC wander with entity collision and terrain step.
5. Per player: the time down when dead (`TickDead`); else action tick (swing hit), potion tick and pending potion start, attack
   lock (`StepPlayerAttack`), stale-motion expiry.
6. `FieldSkills.Step` (cast timelines, projectiles, familiars, pulses, effect timers, regeneration), then place skill
   kill payouts as loot; `FieldPremium.Step` expires premium timers, returns vampiric life and mana per attack and runs
   the premium pets ([special-items.md](special-items.md)).
7. Corpse timers, ground loot expiry.

### Players (`WorldPlayer`)

- Movement is client-owned and validated: a distance budget refills at 1.35 x run speed (village walk speed while the
  character carries more than its weight limit; start 0.5 s, cap 2 s, +32 units slack); an overweight character's
  running report counts as walking; a report is walked in pieces of at most 48 units, each following the wall slide
  within 8 units from the height the last piece reached, so a ramp's rising ground is not a wall. Within a piece the
  height follows the floor in 8-unit steps: a floor within 8 units below is stood on, a deeper one is fallen toward at
  `Falling.UnitsPerSecond` (560) for the time since the last accepted report, and a falling player may pass over ground
  with no floor; each step may climb at most 24 units (the client climbs 10 every 4) and the report must end over a floor. The kept
  height is the server's, never the client's. Busy players (swing, flinch, eat,
  cast) ignore drift up to 192 units and reject larger. A move without an action target drops the attack lock.
- Motion stops showing after 1.5 s without reports. Death clears actions and potion; the player stays down until it
  restarts (below) or a revive reaches it. `Revive` brings a player back in place with a damage guard.
- Death and restart (`Simulation/Progression/DeathPenalty.cs`, `World/RestartTransactions.cs`, `WorldServer.Restart.cs`,
  `Services/RestartAppService.cs`): while down, every owner snapshot carries a `RestartOffer` (field restart open,
  Resurrection Scroll carried, the death's experience and gold shares). A `RestartRequest` (field, town, exit) is a
  durable operation: the tick prepares a candidate that uses up a carried BI104 or else takes the death's share (from
  level 11: a thirty-third of the level's experience span and of the gold, a fiftieth at level 85+ after the level 85
  quest, nothing in the castle field), refuses the field restart when it is closed (castle field; below level 11 outside
  a forest; from level 11 when the experience above the level's start no longer covers a hundredth of the span), and in
  the field takes that hundredth of experience and gold unless the scroll was used. Experience never drops below the
  level's start. The candidate stands up at half life (mana and stamina as they were) at the field's start point
  nearest to where it fell, or at a random start point of its town (Ricarten for Fighter to Pikeman and the assassin,
  Pillai for the Morion classes), on the highest floor there (its height goes in the forced `FieldWarp`); the record is
  stored, then the tick moves the player there, revives it with a 10 s
  guard and sends a revive event. A save while down (leaving the world, disconnecting) stores the town restart with its
  costs instead, so leaving neither skips the penalty nor brings the player back dead.
- Field changes (`MoveToFieldAsync`): the requested position is never used. A gate keeps the current position
  (`CrossGate`); a warp places the player at the catalog exit, settled on the floor. `FieldTransition` accepts a warp
  gate within `max(width, 48) + 64` of the player (level and destination level checked, special effect 2 excluded) or
  a gate of either field pointing at the other within 1810 units. Cooldowns are rebased to the new field clock.

### Monsters, summons and NPCs

- Wild monsters come from `FieldSpawner`; summons are added by `FieldSkills` through `IFieldSkillHost.AddSummon` and
  stepped separately. Summons never count toward population and are never valid basic-attack or lock targets.
- Monster movement re-resolves the AI's desired step against the stage and other live monsters only
  (`MonsterStepping.Step`); players and NPCs do not block monsters. NPCs collide with all entities.
- AI tuning per monster code is cached (`TuningFor`) from its motion timings.
- `WorldNpc` wanders deterministically (LCG seeded from entity id and NPC code) inside `MoveRange` of its home anchor
  when its model has a walk clip; otherwise it stands. Placement converts authored fixed-point (/256) and 4096-step
  angles.

### Combat resolution

Player basic attack:

1. `SetAttackTargetAsync` sets the lock (`AcceptAttackSequence` refuses an older sequence; 0 auto-increments).
2. `FieldCombat.StepPlayerAttack`: when idle and in `AttackRange` (height within 64), `BeginSwing` with timing from the
   class/weapon motion clip and emits `SwingBegin`. The client walks into range; the server waits for that position.
3. `WorldPlayer.TickAction` fires the hit once when elapsed time crosses the clip's event offset (or at clip end).
4. `ResolvePlayerSwingHit`: `SkillFormulas.NormalAttack` + next-hit bonus, `CombatResolver.Resolve`,
   `ModifierRules.ScaleDamage`, skill hooks (`IPlayerSkillCombat.BasicStrike`), then `ApplyPlayerDamage`.
5. `ApplyPlayerDamage` (shared by swings, skills, familiars and summons): records the hit in `MonsterDamageLog`,
   applies damage, boss retarget, emits `Hit` (attacker id is the summon/familiar when `sourceEntityId` is set), flinch
   roll, or on death `OnMonsterDeath`.
6. Kill credit goes to the killer unless the first attacker (latched at full HP) dealt enough damage and hit within
   `KillReward.FirstAttackerDisengageMs` (15000). Experience is `KillReward.ExpForKill(..., ExperienceMultiplier, ExperienceEventPercent)`
   awarded to the credited player; drops are rolled; the corpse despawns after `CorpseDespawnMs` (5710).
7. `KillPayout` drops are placed at the corpse, settled on the floor (`FieldGroundLoot.Place`); each item copy is made
   there with `ItemRoller.Create` from the field's stream (see [simulation.md](simulation.md#items)).

Monster attacks (`ResolveMonsterHit`): basic hits check reach, reflection and evasion hooks, roll with
`CombatResolver.ResolveMonsterAttack` (the player's block chance; a block emits `Block`), scale by modifiers and undead
absorption, pass the skill
`Defend` hook, then `WorldPlayer.ApplyDamage` (ignored during a revive guard), then the family's riders
(`FieldMonsterSkills.AfterBlow`). Skill blows go to `FieldMonsterSkills.ResolveSkill`: a monster's area skill hits every
player inside its shape once per cast (`SkillImpactPending` is consumed before visiting victims). Monster hits on
summons use accuracy versus the summon's defence, the great bosses' multiplier and the summon's absorption. A stunned
player starts no swing and its movement reports are ignored.

### Player skills (`FieldSkills`)

`FieldSkills` is the per-field skill authority: `Cast` validates on the command phase (dead, busy, village,
learned, caster checks, cooldown, target and range with 24 units slack), spends mana and stamina, starts the cooldown
and the cast motion; `Step` fires hits at the motion's event times. Partial files by mechanic: `Damage`, `Areas`,
`Buffs`, `Allies`, `Defense` (hooks into `FieldCombat` via `IPlayerSkillCombat`), `Charge`, `Charm`, `Companions`,
`Resurrection`, `Summons`. Details, data and protocol: [skills.md](skills.md).

### Regeneration and effect timers

`FieldSkills.StepEffects` runs for every player each tick: dead players lose timed effects and reset regeneration;
players in a village lose their timed player-skill effects; then effects step and
`RegenerationState.Step(character, posture, dt)` restores resources by posture (`WorldPlayer.Posture`: standing,
walking, running, acting). Monster effects step too. Formulas live in Simulation ([simulation.md](simulation.md)).

### Summon ownership

One summon per owner (`FieldSkills` maps owner id to summon id); a new call replaces the old one. A summon's
`OwnerId` is published in `EntityView.OwnerId`; its damage credits the owner. It ends when it expires, dies, its
owner leaves the field (`FieldInstance.Remove` -> `FieldSkills.Forget` -> dismiss), is too far to recall, or the owner's
summon status ends. Charmed wild monsters become summons for the charm duration.

### Ground loot (`FieldGroundLoot`)

- At most 1024 drops per field. Non-public drops (monster without `AllPlayersSeeDrops`) belong to the credited player
  for 5 s. Lifetime 90 s for gold and catalog keys starting `PL`/`PS`, 180 s otherwise.
- Clients address drops by the drop's GUID, which `GroundItems` carries and the pick-up command names.
- Pickup reserves a drop by operation id (`TryReserve`, reach 32 units); a reserved drop does not expire. `Resolve`
  removes it on commit or releases it on abort. Inventory-delivered drops (`ToInventory`) throw `NotSupportedException`.
- The reserved copy keeps the rolled values of the drop under the drop's id. A pickup that would carry the character
  over the weight limit is refused with `ItemRuleMessages.OverWeight`.

## Durable operations

Every change to a character's durable state uses prepare, persist, commit under the session's `SaveGate`, through one
runner (`Services/DurableOperations.cs`): `AdmitAsync` takes the gate and returns a `DurableLease` (released on
`Dispose`), and `lease.RunAsync(prepare, commit, reply, failed, store?, abort?)` runs the steps below. Every prepared
record derives from `PreparedOperation` (`Original`, `Candidate`, `Snapshot`). A service maps `DurableFailure`
(`NoSession`, `Busy`, `NotPlaying`, `StorageFailed`, `Conflict`) to its own reply; work that must happen under the gate
before preparing (the warehouse read on open) runs between admission and `RunAsync`.

| Step | Where | What |
| --- | --- | --- |
| Admit | caller thread | `DurableOperations.AdmitAsync`: `SaveGate.WaitAsync(0)` (the quest upkeep waits 2 s); busy if held |
| Prepare | tick (player-owned command) | verify session/connection, replay check, clone (`CloneForInventoryOperation`), apply to the candidate, record the operation, `CharacterRecordFactory.Capture` -> `CharacterSaveSnapshot` |
| Persist | caller thread | `ICharacterRepository.UpdateAsync`, or the service's `store` (NPC: `IWarehouseRepository.SaveWithCharacterAsync`, a refusal is `Conflict`), always with `CancellationToken.None` once admitted |
| Commit | tick (exempt command) | apply the candidate's domain to the live character; throws if the session's character changed |
| Abort | tick (exempt) | the service's `abort`, whenever a snapshot was not stored (a storage exception included): pickups release the ground reservation, the quest upkeep returns its drops |

A prepared operation with `Snapshot == null` is final (rejection or replay) and nothing is written.

| Operation | Prepare | Commit applies |
| --- | --- | --- |
| Inventory move/split/merge/swap/equip/unequip | `WorldServer.PrepareInventoryAsync` -> `InventoryCommands.Apply`; a refusal names the item rule (`InventoryCommands.Refusal`) | inventory, equipment, histories |
| `ConsumePotion` | same; refused when dead or a potion is pending (`PreparedPotionOperation`) | inventory, then `QueuePotion`; the field starts the eat motion and restores at the clip event; a warp before the event drinks it again where the player arrives |
| `Pickup` | `GroundPickupTransactions.Prepare` (`PreparedPickupOperation`) | inventory, gold via `TryChangeMoney`, drop removed |
| `Drop` | `GroundThrowTransactions.Prepare` (`PreparedThrowOperation`): a bag item, `Count` of it, refused for items `ItemDropRules` keeps or a full ground | inventory, then the item laid public where the player stood (`FieldGroundLoot.PlaceThrown`, which may pass the ground's capacity rather than lose a saved item) |
| `DropGold` | the same: `Count` gold, at most what is carried | inventory and money, then the gold taken from the character and laid public where the player stood |
| Progression (attributes, learn skill) | `ProgressionTransactions.Prepare`; learning needs a reachable skill master | re-applies the command to the live character (`ApplyProgression`) |
| NPC services | `NpcServiceTransactions.Prepare` (`PreparedNpcOperation`) | inventory, gold delta, open warehouse, teleport arrival |
| Quest requests | `QuestTransactions.Prepare` (`PreparedQuestOperation`); follow-ups must answer a step the NPC still offers | inventory, equipment, quest journal (merging tick kills), tier, stat/skill point and gold deltas, travel, arena duel |
| Quest upkeep | `QuestTransactions.PrepareUpkeep`, run by the server's `QuestUpkeepService` | quest drops into the pack, quest weapon growth and expiry, leftovers removed; drops return to the player when the write is refused or throws |
| Item use | `ItemUseTransactions.Prepare` with the item's `ItemUseAction` (`PreparedItemUseOperation`) | inventory, premium state, resets and mastery re-applied with the prepared clock, item effects, hair, arrival |
| Personal shop purchase | `PersonalShopTransactions.Prepare` (`PreparedShopPurchase`): the buyer's and the seller's candidates, stored together with `ICharacterRepository.UpdateAllAsync` | both inventories, the price from the buyer to the seller, the seller's listing pruned |

Revisions and idempotency:

- Commands carry a client GUID `OperationId` and an `ExpectedRevision` (`InventoryRevision` or `ProgressionRevision`;
  warehouse actions also `ExpectedStorageRevision`). A stale revision is refused without writing.
- Applied operations are stored per character in five histories (inventory, service, progression, item use, quests),
  each an `OperationHistory<T>` capped at the last 64 and looked up with `Find(operationId)`, and persisted with the character. The same id with an identical command replays as success
  ("already committed") with the recorded revision; the same id with a different command is an "operation identity
  conflict". This makes a retry after a lost reply safe and never charges twice.
- Commit merges only the transaction's domain into the live character (inventory, equipment, histories, money
  delta). Life, experience, skill use counters and position keep changing on the tick during storage IO and are not
  overwritten; the next save persists them.

## NPC services

Flow (`NpcAppService.RequestAsync`):

1. Session lookup, `SaveGate` (zero wait, `Busy` otherwise).
2. Warehouse `Open` only: `CheckNpcServiceAsync` (tick), then the warehouse is read from storage on every open into
   `session.Warehouse` (unreadable data -> `StorageFailed`, cache cleared).
3. `PrepareNpcServiceAsync` (tick): `NpcServiceTransactions.Check` -> NPC exists in the player's field, player alive
   and within `NpcInteraction.Reach` (96, planar), NPC offers the service, the action fits the service, the service is
   available (`NpcServiceAvailability`). Warehouse actions other than `Close` need an open warehouse (`NotOpen`).
4. Non-durable actions (`Open`, `Close`, `Refresh`) return the view: shop catalog (3 tabs) and quotes, warehouse view,
   or teleport destinations. Durable actions check replay, empty id and revisions, then apply to a candidate.
5. Persist: character only, or character plus warehouse in one transaction under the expected warehouse revision.
   A warehouse revision mismatch returns `Conflict` and clears the cached warehouse ("open it again").
6. `CommitNpcServiceAsync` (tick): inventory, `MoneyDelta`, warehouse cache, and for a teleport `Arrive` (move to the
   destination field's first start point; the edge then sends a forced warp).

| Service | Actions | Rules (Simulation) |
| --- | --- | --- |
| Shop | Open, Refresh, Buy, Sell, Repair, RepairAll, Close | `NpcShopRules`: buy own stock at catalog price in its shop form within the weight limit; sell at a fifth of the copy's worn value (no quest items); repair an eighth of lost value; gold cap `MoneyLimits.CarryLimit` |
| Warehouse | Open, Refresh, Deposit, Withdraw, Move, DepositGold, WithdrawGold, Close | `WarehouseStorage`: per account, 9x9 grid, 100 items, weight 1000, 100,000,000 gold; quest items and materials stay on the character; no opening or withdrawing over the weight limit |
| Teleporter | Open, Teleport, Close | `TeleportRoutes`: route 1 only (Mushroom Cave 100, Beehive Cave 100, Cursed Land 500, Forbidden Land 1000 gold); level must reach the destination field's `LimitLevel` |
| Skill master | (dialog only here) | Learning goes through `ProgressionCommand.LearnSkill` with the NPC's entity id; the class change goes through the quests |
| Quest giver, quest script, wing exchange (warp gate 0 or 1) | (dialog only here) | Answered by `QuestAppService` talks, see [quests.md](quests.md) |
| Mobile shop | NPC id 0: Open, Refresh, Sell, Repair, RepairAll, Close | `MobileShopTransactions`: only after a used mobile shop item (`PlayerSession.MobileShopOpen`), no stock |

Other `NpcServiceKind` values answer `NotAvailable`. Prices, stock, totals and destinations always come from the
catalogs, never from the client. `InteractNpcAsync` returns all offers with their availability and one random line.

## Personal shops

A player in a town opens a shop over its own bag items (`PersonalShopRules`, [simulation.md](simulation.md)); the open
listing lives on `WorldPlayer.Shop` and its sign rides on the player's appearance (`PlayerAppearanceView.ShopSign`, part
of the descriptor revision). Nothing of it is stored: a shop closes when its seller closes it, sells its last unit,
dies, leaves or is moved (`Relocate`). Opening and closing hold the seller's `SaveGate` (`Busy` while a purchase holds
it), so a listing never changes between a purchase's prepare and commit.

- An open shop holds its seller: movement reports keep it where it stands (as a swing does), and attacks, skill casts,
  item uses, NPC services (the mobile shop included) and every inventory command but a potion are refused
  (`PersonalShopStatus.Selling`). A drunk potion prunes the listing.
- A view (`ViewAsync`) needs the seller in the viewer's field within `NpcInteraction.Reach`, as an NPC is reached, and
  answers the listing as the viewer sees it (`PersonalShopListing`: each copy's values for the viewer's class).
- A purchase (`BuyAsync`) admits the buyer's `SaveGate`, finds the seller's session on the tick, then admits the
  seller's gate without waiting (`Busy` otherwise, after answering a retried purchase's first outcome, so two players
  buying from each other never block). The tick
  checks replay first (the buyer's `ShopPurchases`), then the shop, reach, revision and `PersonalShopRules.TrySell` on
  clones of both characters; both snapshots are written in one transaction, then the commit applies both inventories
  and the gold, prunes the listing and answers what remains on sale. Both players read the sale as a trade chat line.
  A purchase that met another price, fewer units or a sold item answers what the shop offers now.

## Published outcomes and events

- Command results return through the `Task` of the call (`MovementVerdict`, `SkillCastOutcome`, `InventoryOutcome`,
  `ProgressionOutcome`, `NpcServiceReply`, `NpcInteraction`).
- Per tick and field, one `WorldSnapshot`: server tick, `EntityView[]` (players, monsters, NPCs), `CombatEventView[]`
  (`SwingBegin`, `Hit`, `PotionBegin`, `PotionApplied`, `MonsterSkillImpact`, `Heal`, `Evade`, `Revive`),
  `SkillEventView[]` (start, impact, cancel, release), `StatusEventView[]` (add, refresh, remove, tick), ground drops
  with owner and visibility, the frame's `EntityGrid`, the frame itself with its generation, the field's running
  statuses (`ActiveStatuses`) and clock (`FieldNowMs`). `WithOwner` stamps each copy with the receiver's `OwnerState`
  (only
  when changed) and its cooldowns (null when unchanged since the last send); each copy also carries the receiver's
  `QuestUpdate` (journal view when its revision moved, quest notices, a dialog the world opened).
- `WorldOutbox` is the per-connection `Channel<WorldSnapshot>`: a write never blocks; the reader receives the latest
  entity state plus every event accumulated since its last read (state is coalesced, events are reliable, the newest
  cooldown list wins; quest notices stay in order up to 256 and the newest journal view and dialog win). A reader that
  kept up gets the field's own event arrays; one that fell behind gets them joined in order with `EventSegments`
  telling which tick each part belongs to. Reading pins the snapshot's frame (`ReleaseFrame` after encoding); a frame
  the field already reused yields a snapshot without entities. More than 4096 pending events completes the channel with
  an `InvalidOperationException`; the edge closes the connection.
- Statuses: the field keeps the last published `Add`/`Refresh` of every running status (`FieldStatusBook`), removes
  them on `Remove`, when the entity leaves (`TransferPlayer` carries a player's statuses to the new field on its clock)
  and when they run out (pruned every 5 s), so an encoder can send them to a viewer that starts seeing the entity.
- `IServerStatusReadModel` (`WorldStatusReadModel`) reads volatile counts without touching the tick.

## How to

Add a player command (tick-only, not durable):

1. Add a public method on `WorldServer` that calls `EnqueueAsync(entityId, ...)` with the player's entity id so it
   counts against the quota; validate the connection and field inside the closure; never await inside it.
2. Put field-local logic on `FieldInstance` (or `FieldSkills`/`FieldCombat`) and emit results as events if other
   players must see them; add a `CombatEventKind`/`SkillEventPhase` value (Contracts) only together with its encoding.
3. Map the packet in `Server/PristonTale.Server/Net/ClientSession.cs`, catch `WorldBusyException`, and document the
   opcode in [protocol.md](protocol.md) and the edge in [server.md](server.md).
4. Add a regression case in `tests/PristonTale.Tests` (e.g. `NetworkHardeningTests.cs` for quota behaviour); the runner
   discovers its class's `Cases()`.

Add a durable character operation:

1. Command, outcome and applied-history types in Simulation with an `OperationId` and expected revision.
2. A `*Transactions.Prepare` that returns a `PreparedOperation` (usually `PreparedCharacterOperation<T>`) (replay check first, clone, apply,
   record, capture snapshot), a `WorldServer.Prepare*Async` (player-owned) and `Commit*Async` (exempt, merges only
   the operation's domain, throws on ownership change).
3. A service method that admits with `DurableOperations.AdmitAsync` and calls `lease.RunAsync` (inventory and
   progression go through `SessionAppService.ApplyDurableAsync`); if it reserves world resources, release them in its
   `abort`.
4. Persist the new history in `CharacterRecordFactory.Capture`/`CharacterStateCodec` ([persistence.md](persistence.md)).

Add an NPC service or action:

1. `Shared/PristonTale.Simulation/World/Npcs/NpcServiceKind.cs` and `NpcServiceCommand.cs`: append enum values
   (both are persisted; never renumber), add reply fields to `NpcServiceReply` and statuses to `NpcServiceStatus`.
2. Turn it on in `NpcServiceAvailability.IsAvailable` (`TeleportRoutes.cs`) and put rules in Simulation.
3. `World/NpcServiceTransactions.cs`: `Accepts`, the `Apply` switch for durable actions (non-durable ones in `Show`),
   `Describe` for new statuses. Gold changes go through the candidate so `MoneyDelta` is computed.
4. Edge and client: `Server/PristonTale.Server/Net/NpcWireMapping.cs`,
   `Shared/PristonTale.Protocol/Packets/NpcPackets.cs`, `Client/PristonTale.Gameplay/UI/Npc/HudNpcServiceController.cs`;
   if the authored data maps a new key, the importer ([world-import.md](world-import.md)).
5. Tests in `tests/PristonTale.Tests/NpcServiceTests.cs`; update the service table above.

## Pitfalls

- `WorldBusyException` is thrown synchronously by the `WorldServer` call, not delivered through the returned task;
  catch it at the call site (a fire-and-forget `_ = world.X(...)` still throws).
- Commit commands must stay exempt (`NoOwner`, `waitForCapacity`): refusing a commit after a successful write would
  leave storage ahead of memory.
- Storage writes use `CancellationToken.None` on purpose; a cancelled admitted write is recovered by retrying the
  same operation id.
- `CommitInventoryOperation` does not copy money; gold changes are applied as deltas (`TryChangeMoney`). Copying the
  whole candidate would erase tick-side changes made during storage IO.
- Field clocks are per instance. Anything stored as an absolute field time (cooldowns, statuses) must be rebased when
  a player changes field; `TransferPlayer` does both. Every move between fields goes through `WorldServer.Relocate`
  (gates and warps, NPC and item teleports, wing gates, quest travel, restarts, game-master moves): it transfers only
  when the field differs, updates the connection and the session's placement, and the caller then stands the player
  (`EnterField`, `CrossGate` or a restart).
- `FieldInstance` mutates only during its own step or in the command phase; never call into a field from another
  field's step (they run in parallel).
- `SessionRegistry.Remove` releases the character claim; a session that leaves the world is closing (state 4) and
  cannot enter again.
- `SubscriberPresence` is not the production diff; the server's `PresenceEncoder` has its own. Keep both consistent
  when adding `EntityView` fields that need a full respawn.
- `CharacterSummary` building throws on inconsistent persisted equipment so corruption is visible; do not swallow it.
  The stored weapon pair (`SavedItem.Hidden`) shares the hand slots with the held one and is skipped: the summary shows
  the held pair.

## Related

- [architecture.md](architecture.md) - layering and the process/threading overview
- [server.md](server.md) - host, TCP edge, configuration and the settings table
- [protocol.md](protocol.md) - packets for inventory, progression, NPC services, skills, presence
- [skills.md](skills.md) - skill data and `FieldSkills` resolution
- [simulation.md](simulation.md) - characters, items, combat formulas, modifiers, regeneration
- [world.md](world.md) - spawning, AI, loot rolls, NPC catalog, terrain
- [persistence.md](persistence.md) - repositories, character and warehouse records
- [testing.md](testing.md) - regression runner
