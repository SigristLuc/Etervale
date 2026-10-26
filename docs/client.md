# Client

The game client: the executable (`Client/PristonTale`), the screen layer, game scene, HUD, remote world and audio
(`Client/PristonTale.Gameplay`) and the client side of the protocol (`Client/PristonTale.Client.Net`). Shaders,
rendering internals, skill presentation and VFX data have their own documents (see Related).

## Read this when

- You add or change a screen, a HUD window, a key binding or a mouse interaction.
- You handle a new server message on the client, or change how presence, combat or owner state reaches the HUD.
- You change local movement prediction, movement reports, warps or server corrections.
- You touch remote actor playback (interpolation delay, extrapolation, spawn deferral) or the target frame.
- You add asset loading to the client and need to keep it off the frame.
- You debug login, character select, world entry or the shutdown path.

## Code map

| Path | Responsibility |
|---|---|
| [`Client/PristonTale/Program.cs`](../Client/PristonTale/Program.cs) | Entry: no arguments, `--settings PATH`, or `--validate-display REPORT.json` (runs `DisplayValidationProbe`) |
| [`Client/PristonTale/Game1.cs`](../Client/PristonTale/Game1.cs) | MonoGame `Game`: device setup, variable timestep, asset root discovery, server address, Escape/exit, window-title stats, shutdown gating |
| `Client/PristonTale/Display/` | `PresentationController` (settings at the frame boundary, display confirmation, pacing, save), `DisplayController` (window mode, size, VSync; primary monitor only), `DisplayValidationProbe` |
| `Client/PristonTale/Content/Content.mgcb` | Content build: the shader library (see [shaders.md](shaders.md)) and `Fonts/UI.spritefont` |
| `Client/PristonTale/PristonTale.csproj`, `Client/MonoGameRuntime.targets` | References the MonoGame Vulkan fork in `monogame/`; copies the native runtime next to the executable |
| `Client/PristonTale.Gameplay/Screens/Framework/` | `GameClient` (owns bootstrap libraries, backend and the current screen), `GameScreen` (base), `ScreenContext` (shared services) |
| `Client/PristonTale.Gameplay/Screens/Opening/` | Opening cinematic, login form overlay and login polling |
| `Client/PristonTale.Gameplay/Screens/CharacterSelect/` | Character list, creation flow (race, job, face, name), delete, enter world; its own renderer, scene and camera |
| `Client/PristonTale.Gameplay/Screens/Gameplay/GameplayScreen.cs` | Wires `GameScene` and `GameplayHud`, debug keys, render-stats status text |
| [`Client/PristonTale.Gameplay/World/GameScene.cs`](../Client/PristonTale.Gameplay/World/GameScene.cs) | The in-world scene: fields, local player, input, prediction, movement reports, warps, presence drain, draw |
| `Client/PristonTale.Gameplay/World/GameScene.Skills.cs` | Skill casting from the mouse sockets and the skill timeline glue ([skills.md](skills.md)) |
| `Client/PristonTale.Gameplay/World/Streaming/` | `FieldResourceStreamer` (off-thread field preparation, budgeted GPU upload), `FieldStreamingPolicy` (prefetch and retention distances, budgets) |
| `Client/PristonTale.Gameplay/World/Remote/` | Remote actors: `RemoteEntityManager`, `RemoteSnapshotBuffer`, `RemoteServerClock`, actor composers, equipment visuals, ground loot, picking |
| `Client/PristonTale.Gameplay/World/*.cs` (other) | `TargetSelection` (target frame owner), `NpcApproach`, `GroundTraversal`, `FieldCollisionQuery`, `LoadedField`, `EnteredCharacter` |
| `Client/PristonTale.Gameplay/Combat/` | `ActorMotionController` (local motion state machine), `AttackInputController` (attack press/release and server reconcile) |
| [`Client/PristonTale.Gameplay/UI/GameplayHud.cs`](../Client/PristonTale.Gameplay/UI/GameplayHud.cs) | In-game HUD root: windows, input routing, draw order, NPC service windows |
| `Client/PristonTale.Gameplay/UI/` (root) | `HudLayout` (800x600 design space), `LoginPanel`/`LoginLayout`/`TextInputField`, `DisplayConfirmation` |
| `Client/PristonTale.Gameplay/UI/Widgets/` | Bottom bar, status window, skill tree and sockets, minimap, target frame and combat history (`HudCombat`), 3D target portrait, NPC dialog, settings panel, system menu, notices |
| `Client/PristonTale.Gameplay/UI/Chat/` | Chat window, tabs, input line and speech bubbles (see [chat-and-gm.md](chat-and-gm.md)) |
| `Client/PristonTale.Gameplay/UI/Inventory/` | Inventory window layout, drag/drop and equip commands |
| `Client/PristonTale.Gameplay/UI/Npc/` | NPC service controller, shop and warehouse windows, store layout, NPC art |
| `Client/PristonTale.Gameplay/UI/Quests/` | Quest controller, quest dialog and yes/no boxes, quest window, quest texts and art (see [quests.md](quests.md#client)) |
| `Client/PristonTale.Gameplay/UI/Rendering/` | `HudSprites` (original UI images), `HudIconCache` (item icons), `HudAtlas` (login atlas), `HudPainter`/`HudCanvas` |
| `Client/PristonTale.Gameplay/Audio/` | `PristonAudioLibrary` (bootstrap sound load), `FieldAudioController` (music, field and positional ambience), `NpcAudioController`, `TalkSound`, `CombatSounds` and `CharacterSoundBanks` (character, weapon and footstep sounds), `MonsterSoundCues`, `ItemSoundRules` (item sounds) |
| `Client/PristonTale.Gameplay/Effects/` | Effect glue: `EffectLibrary`, `VfxContent` (effect data from `<assetRoot>/Effect/Vfx`, loaded off the game thread), `SkillPresentation`, `ScriptEffects`, `SkillSoundBank`, `SkillState`, combat text, meteor, monster mesh and return effects ([effects.md](effects.md)) |
| [`Client/PristonTale.Client.Net/GameServerConnection.cs`](../Client/PristonTale.Client.Net/GameServerConnection.cs) | TCP session: connect, login, characters, world entry, request correlation, heartbeat, receive dispatch |
| `Client/PristonTale.Client.Net/IGameBackend.cs`, `IPlayChannel.cs` | Contracts the screens use: account and character requests; in-world commands and the presence stream |
| `Client/PristonTale.Client.Net/Online/` | `OnlinePlayChannel` (world session lifetime), `PresenceDecoder` (wire packets to `PresenceUpdate`) |
| `Client/PristonTale.Client.Net/Presence/` | Neutral presence model: `PresenceUpdate`, spawns, states, combat and skill events, `RemoteMoveState` |
| `Client/PristonTale.Client.Net/WorldCatalog/` | `WorldRenderCatalog`: monster and NPC templates learned from the server |

## Startup

1. `Program` builds `Game1` with an optional settings path (default `ClientSettingsStore.DefaultPath`, the local
   application data folder, `PristonTale/client-settings.json`; fields in [client-settings.md](client-settings.md)).
2. `Game1` constructor: HiDef device, `PreferMultiSampling = false` (the renderer owns its multisampled HDR target),
   `IsFixedTimeStep = false`, resizable window, `Content.RootDirectory = "Content"`.
3. `Game1.LoadContent` creates `PresentationController` and `GameClient(GraphicsDevice, Content, ResolveAssetRoot(),
   "tcp://localhost:10009", presentation)`. The server address is the `Game1.ServerAddress` constant; there is no
   setting for it.
4. `GameClient.LoadContent` loads, synchronously on the game thread: the UI font, `HudAtlas.LoadBootstrap`,
   `PristonSkyLibrary.LoadBootstrap`, `EffectLibrary.LoadBootstrap`, `PristonAudioLibrary.LoadBootstrap`. It then
   creates the single `GameServerConnection` (not connected yet), the `ScreenContext` and the first screen,
   `OpeningScreen`.

### Asset discovery

- `Game1.ResolveAssetRoot` walks up from `AppContext.BaseDirectory` and returns the first ancestor's `assets/`
  directory; if none exists it falls back to `<base>/../../../../assets`. Running from `Client/PristonTale/bin/...`
  finds the repository's `assets/`.
- Every asset path is relative to that root (`ScreenContext.AssetRoot`, `GameScreen.AssetPath`). Original assets are
  read through `PristonFileResolver` (`Client/PristonTale.Engine3D/IO/PristonFileResolver.cs`), which resolves
  separators and rebuilds the real casing component by component, caching hits; use it (`Resolve`, `Exists`,
  `ReadAllBytes`, `OpenRead`) instead of `File.*` for any path into `assets/`.
- Field definitions (stage `.smd`, scenery models, gates, warp gates, lighting, camera, music) come from the shared
  `WorldCatalog` in `Shared/PristonTale.Simulation/World/WorldDefinitions.cs`, not from the server. Monster and NPC
  models come from the server (`WorldRenderCatalog`, below).

## Screen state machine

```
OpeningScreen --login ok--> CharacterSelectScreen --enter world ok--> GameplayScreen
      ^                              |
      +------- Exit button ----------+
```

- `GameScreen` lifecycle: `Enter` (after the previous screen is disposed), `Update`, `Draw`, `HandleEscape`, `Dispose`.
- A screen requests a transition with `Context.RequestScreen(next)`. `GameClient` stores it as pending and swaps after
  the current `Update` returns: `Dispose` old, then `Enter` new. Never swap from `Draw`.
- `OpeningScreen`: cinematic (`StartImage/Opening/Map/stage.smd` and its camera path); any key or the end of the
  cinematic reveals the login form. `OpeningLoginController` starts `IGameBackend.LoginAsync` and polls the task each
  frame. The server accepts one login per connection, so when `IGameBackend.RequiresNewConnection` is set (a login
  succeeded or the connection closed) the controller first calls `ScreenContext.RenewBackend`: `GameClient` installs a
  new `GameServerConnection` and disposes the old one off the game thread.
- `CharacterSelectScreen`: sub-states `CharacterSelect`, `RaceSelect`, `JobSelect`, `FaceSelect`, `Name`. The roster
  shows each character in its armour, empty-handed. While a
  `Busy` operation (roster load, roster/job actor preparation, enter, delete, create) runs, input is ignored and
  `PollBusy` checks the task. A successful `EnterWorldAsync` builds an `EnteredCharacter` (class, name, level, face,
  field code, spawn x/z, tier, hair) and requests `GameplayScreen`. The Exit button returns to `OpeningScreen`; the next
  login opens a new connection.
- `GameplayScreen` has no transition out; leaving it means quitting.
- Escape: `Game1` calls `GameClient.HandleEscape` -> `GameScreen.HandleEscape`. `GameplayScreen` closes the open HUD
  panels (`GameplayHud.ClosePanel`) and returns true if anything was open; every other case exits the game. While a
  display change awaits confirmation, Escape reverts it and Enter confirms it instead.

### Shutdown

`Game1.OnExiting` cancels the exit until `GameClient.BeginShutdown` and `PresentationController.CompleteAsync`
(settings write) finish. `BeginShutdown` disposes the screen on the game thread (GPU resources) and disposes the backend
on the thread pool; meanwhile `Draw` shows "Saving and disconnecting…". Disposing `GameScene` disposes the play channel,
which sends the Etervale leave-world request and waits up to 5 s for its reply so the server saves first.

## Frame loop

`Game1.Update` -> `GameClient.Update` (audio settings, display confirmation, `screen.Update`, pending screen swap).
`Game1.Draw` -> `GameClient.Draw` -> `screen.Draw` and the confirmation overlay. `Game1.EndDraw` presents, then lets
`PresentationController` capture the window size, apply changed settings (`UpdateBoundary`) and pace the frame
(`Pace` sleeps toward the `maxFps` deadline; this is the frame limiter, after present).

`GameplayScreen.Update` order: hand a fetched catalog to the HUD, build `HudWorldView`, `GameplayHud.Update` (sets
`PointerCaptured`), copy HUD state into the scene (pointer capture, camera input enabled, mouse socket skills), debug
keys, `GameScene.Update`, copy the scene's selected target back to the HUD.

`GameScene.Update` order: NPC audio, equipment presentation, transient effects, field audio, one streaming upload step,
`DrainPresence`, skills, `UpdatePlayerInput` (picking, prediction, NPC approach, loot, attack approach), hover target
selection, warps, streaming scan, camera, renderer, movement report, field `OnUpdate` hook.

`GameplayScreen.Draw` order: `GameplayHud.RenderPortrait` (must be first: it binds its own render target, which discards
the back buffer), clear, `GameScene.Draw` (renderer, effects, `ComposeScene`), ground loot labels, floating combat text,
`GameplayHud.Draw`.

### What stays off the frame

`Update` and `Draw` never wait on network, disk, tasks or long locks (AGENTS.md). The patterns in use:

| Work | Off-thread part | Game-thread part |
|---|---|---|
| Server requests (login, characters, inventory, progression, NPC, services) | `Task` from `IGameBackend`/`IPlayChannel` | Poll `IsCompleted` each `Update`, or read a `volatile` result set by a continuation (`HudInventoryController`) |
| Upstream sends (movement, attack, skill cast) | `SendUpstream` attaches a fault logger; the connection enqueues into its send buffer | Fire and forget |
| Presence stream | `GameServerConnection` receive loop -> bounded channel -> `GameScene.ReadPresenceAsync` (`Task.Run`) -> `_presenceQueue` | `DrainPresence` reads at most 64 updates per frame |
| Field streaming | `FieldResourceStreamer.Request`: stage and model parse, collision build, `PrepareStageTexturesAsync` on `Task.Run` | `Update` publishes through `AddStageIncrementally`: up to 32 steps and a 2 ms soft budget per frame |
| Remote actors | Composer `PrepareAsync` (`PristonActorLoader.Load` + `PrepareActorResourcesAsync`) | `RemoteEntityManager.Update` publishes one completed preparation per frame |
| Ground loot models | `GroundLootPresentation` preparation tasks | `GroundLootPresentation.Update` |
| HUD images and item icons | `HudSprites`/`HudIconCache` decode on the thread pool | Upload within a byte budget per `Update` (`HudSprites`: 512 KiB). The icon cache keeps only the last requested set (at most 512), so `GameplayHud.RequestIcons` always names the pack, the open shop and the open warehouse together |
| UI bitmap transparency | `PristonImageDecoder.Decode(..., bmpCoverage: true)`, `PristonUiPixels.Prepare` | A 32-bit BMP whose fourth byte varies (not all 0, not all 255) carries its coverage, which replaces the black key (such as item `DA304`, drawn over a near-black background); other BMPs take exact black as transparent; world textures stay opaque |
| Skill sounds | `SkillSoundBank` reads and decodes on a worker | Creates `SoundEffect`s in `Update`; a sound not ready is skipped |
| Display settings file | `ClientSettingsStore` writer task | `QueueSave` only |

Synchronous loads that are accepted because they happen at a screen boundary: `GameClient.LoadContent` bootstrap
libraries, the opening and character-select stages in `Enter`, and the first field in `GameScene.LoadEntered`.
`GameScene.BeginRemotePresence` then calls `PristonRenderer.RequirePreparedTextures()`: from that point the texture
loader throws if a texture reaches upload without an off-thread preparation, which catches accidental disk reads on the
frame.

## Network client

`GameServerConnection` implements `IGameBackend` over one TLS connection (`TlsTransport.ConnectAsync`, then
`FrameConnection`; `Shared/PristonTale.Protocol/Transport`). The address is `tcp://host:port`, optionally with
`?cert=<sha-256 hex>` to pin the server certificate. Wire details live in [protocol.md](protocol.md).

| Step | Client sends | Completes on |
|---|---|---|
| Connect | First `LoginAsync` opens the socket and starts `RunAsync` (receive) and `HeartbeatAsync` (`Heartbeat` every 4 s) | `ServerHello` (minimum protocol, server name for the character screen) |
| Login | Encrypted `Login` (protocol version, account, password) | `LoginResult` after the `CharacterList`; a non-zero status, or a `ProtocolVersion.Current` below the hello's minimum, is a denial |
| Characters | `ListCharactersAsync` returns the list cached at login; create/delete send `CharacterCommand` | `CharacterResult` (a fresh list arrives first on success) |
| Enter world | `EnterWorld` with the name | `WorldEntry` (id, field, spawn point; a non-zero status is a denial) |
| Open play | `OpenPlayChannel` -> `OnlinePlayChannel` sends `JoinWorld` with the entered id | `WorldJoined` |
| Leave | Disposing the play channel sends `LeaveWorld` | The answer with Saved 1 (5 s timeout) |

Correlation rules (requests without an operation id):

- Login, character and world-entry requests, open play and leave are serialised by `_requestGate` and complete on
  their reply opcodes.
- Inventory and progression commands carry a `Guid` operation id; `InventoryResult`/`ProgressionResult` complete the
  matching entry in `_operations`.
- NPC interaction is an `AttackRequest` on the NPC serial; one request per NPC serial at a time; `NpcResult` completes it.
- NPC service requests carry an operation id. Shop catalog, quotes, warehouse and teleport list messages arrive before
  `NpcServiceResult` and are collected on the pending `ServiceRequest`, then returned together as `NpcServiceReply`.
- A receive-loop failure fails every pending request and completes the presence channel with the error; later calls
  throw `IOException`.

Sends from any thread take `_sendGate` only to serialise into the shared scratch buffer; `FrameConnection` appends to
a pending buffer flushed by a single writer task (a backlog above 512 KiB closes the connection).

### Presence decoding

`HandleAsync` (receive thread) offers every packet to `PresenceDecoder.TryHandle` first; presence packets become a
`PresenceUpdate` published to a bounded channel (4096, `TryWrite`; overflow throws and ends the connection).

- `PresenceEntries`: full and motion entries update tracked objects (a motion entry never introduces one). An unknown serial or a
  changed descriptor revision triggers `DescribeEntity`; an object is published as a spawn only once its `EntityDescription`
  arrived. The connection's one-second timer
  (`RetryDescriptions`, which shares the decoder's lock with the receive thread) asks again for every object still
  undescribed after a second: the server drops requests beyond its bucket, and an object that stands still sends no
  entry that would ask again.
- `EntityDescription`: monster and NPC descriptions register a `MonsterRenderInfo`/`NpcRenderInfo` in `WorldRenderCatalog`
  (model path, scale, name, level, brood, NPC services, sound code, portrait offsets) and are shared by template code;
  a summon's or crystal pet's description (user brood, its owner) is never shared. A new description is re-published
  as a spawn of a known id (full refresh).
- Owner state: `OwnerStatus`, multi-part `Inventory` and `Skills` are combined into one `OwnerState` only when the
  status revisions match the received inventory and skills revisions. `OwnerItemDecoder` keeps the `ItemDetails` parts
  of a revision (attached to the items as `OwnedItemView.Details`) and the latest `OwnerItemStatus` (weight, critical,
  block, range, regeneration, resistances), folded into each published owner state.
- Also decoded: `RemoveObjects`, `CombatEvents`, ground items (`GroundItems`/`GroundItemsRemoved`), `FieldWarp`, `PresenceClock` (sets the
  server time stamped on later updates), skill events, status effects, cooldowns and cast results
  ([skills.md](skills.md)).

`IGameBackend.GetWorldCatalogAsync` returns the live catalog instance immediately; it fills as descriptions arrive.

### Presence on the game thread

`GameScene.DrainPresence`, per update: record field id, replace `Owner`, `RemapToWorld`, `RemoteEntityManager.Apply`,
apply a warp, ground loot, effects, skills, then raise `PresenceObserved` (the HUD target frame, combat history and chat lines;
a `CloseRequested` update ends the game).

Coordinates: the wire uses PT-global `(x, z)` in fixed point (`PristonUnits.FixedScale` = 256) and angles of 4096 per
turn; the client world uses `(x, -z)`. `RemapToWorld` flips only spawns, states and the warp position (and facing).
Combat events, skill events, ground items and the `PresenceObserved` copy keep transport coordinates; their consumers
flip Z themselves.

### Remote actor playback

- `RemoteServerClock` maps local frame time onto the server clock from `PresenceClock`; early packets move the offset
  quickly, late ones slowly; a jump above 1 s resyncs. Without the extension samples are stamped with arrival time.
- `RemoteSnapshotBuffer` (per entity, 32 samples) renders at `now - delay`. The delay starts at 150 ms and follows the
  observed sample cadence up to 600 ms. Position, height and facing interpolate linearly; a Walk/Run sample may be
  extrapolated for 250 ms, then the lead returns to the last sample over 250 ms. A sample more than 256 units from the
  last or drawn position snaps and drops the history. Motion states apply when render time reaches their sample.
- `RemoteEntityBase` owns no rules: the server owns position and motion. Presentation-only choices: Walk/Run playback
  speed follows the interpolated ground speed; Walk/Run without ground motion shows Stand. Combat events share the
  sample timeline.
- `RemoteEntityManager`: the self entity's spawn/state/combat events go to `GameScene` sinks, not to an actor. A monster
  or NPC whose template is not in the catalog yet is held in `_pendingModels` until `SetCatalog`; a spawn whose models
  are loading waits in `_preparingActors` and keeps receiving the latest state. A player appearance change re-composes
  the actor from the drawn pose. Summons record their owner and are not pickable; summons and crystal pets carry
  a name plate (`SummonPlates`, drawn after the ground labels) at the model's authored height.
- `GroundLootPresentation` lays each drop as the original does: a yaw drawn from its position
  (`(x + z) >> 2` in fixed point, 4096 per turn), and weapons (`0x01xxxxxx` codes) turned a quarter turn about X so they
  lie flat. The turned mesh rests on the floor by its lowest rotated corner (`GroundLootPresentation.Orientation`).

## Local player: prediction and authority

The client owns its own locomotion; the server validates and corrects.

- Click-to-move: `UpdatePlayerInput` advances `DestinationMovement` at `Locomotion` walk/run speed derived from the
  server-reported move speed (local derived stats until the first self state), slides against field collision
  (`MoveWithCollision`) and entity neighbours (`ResolveEntityCollision`) and follows the floor (`GroundTraversal`).
  Target reachability is checked with `GroundTraversal.CanReachSurface`.
- Reports: `UpdateMovementReport` sends `MovementReport` (position in PT-global, facing, moving, running, action target,
  field code) every 200 ms while moving, every 1000 ms idle, and at once when motion, run mode, target or field
  changes, or when `_reportDue` is set, never twice within 50 ms (`MinReportGapMs`: the server closes a connection over
  251 packets in 5 s). The walk toward a skill target reports only a new destination (`ApproachReportDistance`).
- Server echoes of the self state do not move the avatar. They update HP, move speed, attack rate, the attack-target
  reconcile (`AttackInputController.Reconcile` by sequence) and authoritative motions (Attack, Damage, Dead, Eat,
  the restart after death). The last echoed position is kept as `_serverPlanarPosition` for reach checks (NPC dialog,
  loot pickup).
- Only `FieldWarp` moves the avatar: a forced warp (server rejected a report) or a difference above 200 units snaps the
  body and camera and stands it on the floor nearest the warp's height (within 64 units, else the highest floor). A warp
  to another field code activates that field if loaded; otherwise it requests the field and holds the warp
  (`_pendingFieldWarp`, the player stops) until the field is in the world, then applies it.
- Field changes are local: across a seamless border the active field becomes the loaded field with the highest floor
  under the player (`UpdateActiveField`); warp gates are detected in `UpdateWarps` (level limit checked), and
  special-effect warp gates hold the player for 2 s with the return effect and portal sound before switching. A wing
  warp stone (`GameScene.WingGates.cs`) holds the player on it and opens the wing warp map; walking off closes it, and
  it opens again only after the player left the stone. Choosing a destination plays the return sparkle and the portal
  sound on the stone, and the request leaves two seconds later, as the original's wing trip does. The
  server learns the new field code from the next report, which a field change sends at once.
- `ActorMotionController`: `ApplyLocomotion` is ignored while Damage, Dead or Eat freeze locomotion;
  `ApplyAuthoritative` always wins.
- NPC click: `NpcApproach` walks the player to 0.65 x `NpcInteraction.Reach`, sends a stop report, and opens the dialog
  only when the server-echoed position is within 0.9 x reach.
- Loot click: `GroundLootInteraction` walks to the drop and sends an `InventoryAction.Pickup` command once both the
  local and server positions are within 28 units.

## Field streaming

The active field is the loaded field with the highest floor under the player. Every 8 frames `ScanStreaming` requests
neighbours whose gate seam, or warp-gate destinations whose gate, is within 1024 units, or ahead of a move target
within `PrefetchDistance(speed)` (1400 to 4500 units); fields beyond `RetentionDistance` (prefetch + 1200) unload. At
most 2 fields prepare at once; a field that failed once is not retried. `Warp` unloads every field but the destination.
Renderer side of stage publication: [rendering.md](rendering.md).

## Input

| Input | Where | Effect |
|---|---|---|
| Left click | `GameScene.UpdatePlayerInput` | In order: pick ground loot, cast the left socket skill, pick a monster (attack) or NPC (approach and talk), move |
| Double left click (350 ms) on the ground | same | Keep walking toward the cursor without holding the button, until blocked or the next click |
| `R`, or the bar's walk/run button | `GameplayHud.RunToggleRequested` -> `GameScene.ToggleRun` | Walk/run setting (running at first) for every move: clicks, monster, NPC and item approaches. A player tracing a target or holding the button keeps its pace while its stamina is at a quarter or less and walks once it runs out; overweight only walks |
| Shift held while running (game masters) | `GameScene.MovementSpeed` | Dash at 3000/256 units per original tick; `WorldJoined` carries the game master level and the server allows game masters that speed |
| Shift + click on a monster | same | Automatic attack (keeps attacking after release) |
| Right click | same | Attack if the right socket holds the basic attack, otherwise cast the right socket skill |
| Hold `A` | `GameScene.DrawGroundLabels`, `TryPickLoot` | Show and pick every ground item label |
| Middle drag, cursor at screen edge, wheel | `PristonCamera.UpdateOrbit` (Engine3D) | Orbit, edge orbit, zoom; ignored while the HUD captures the pointer |
| `I` or `V`, `C`, `S`, `X`, `Q` | `GameplayHud.UpdateKeys` | Inventory, status, skills, system menu, quest window (ignored with Ctrl held) |
| `F1`-`F8` | `HudSkillBindings` | Select a skill shortcut; while pressing a skill in the tree, bind it ([skills.md](skills.md)) |
| `Enter`, `Page Up`/`Page Down`, `Up`/`Down` | `HudChat.Update` | Open the chat input or send its line, scroll the chat, recall commands; while typing, HUD, binding, debug and motion keys and the `A` labels are suppressed |
| `Esc` | `Game1` | Close the chat window first, then HUD panels, else quit |
| `Ctrl+F3` / `Ctrl+F5` / `Ctrl+F10` | `GameplayScreen.UpdateDebugKeys` | Toggle render stats in the window title / toggle shadows / cycle `PristonDebugView` |
| `,` / `.` / `P` | same | Online the sky follows the server's game clock: shift it -1 h / +1 h on this screen, or hold it; offline time of day -1 h / +1 h / toggle the day cycle (150 s) |
| `Ctrl+1`..`Ctrl+7` | `GameplayScreen.UpdateMotionKeys` | Force Stand, Walk, Run, Attack, Damage, Dead, Skill on the local avatar |

`GameplayHud.PointerCaptured` (pointer over a HUD element or dragging, or any of the NPC and quest dialogs, message
boxes, the wing warp map, the Teleport Core list, the warehouse, aging and mixing windows open, as the original stops the
character under them) disables world clicks, hover picking and camera
mouse input for that frame. Keys are polled with `Keyboard.GetState()` and edge-detected against the previous frame per
consumer. Typed characters come from the window's text input (`Game1` to `GameClient.TypeCharacter` to
`ScreenContext.TypedText`) and reach `TextInputField` on the login and name forms and the chat input line.

## HUD and windows

- Layout: the HUD is authored in 800x600 logical units. `HudLayout.Create` picks the largest integer scale that fits;
  bottom-anchored elements move down by `BottomOffset`, right-anchored ones right by `RightOffset`. Convert the mouse
  with `HudLayout.ToLogical`.
- `HudPainter` draws original sprites at the integer scale with point sampling and queues text into a linear-filtered
  pass; call `Flush` before drawing a layer that must cover earlier text.
- Draw order (`GameplayHud.Draw`): speech bubbles, minimap, target frame, chat window (shrunk while a side window is
  open), status / skills / inventory windows (they share one screen area; opening one closes the others, `OpenOnly`),
  quest window, shop, warehouse, bottom bar, skill sockets, buffs and the quest timer icon, hover text, system menu,
  quest dialog or NPC dialog, settings, item overlays, quest yes/no boxes, notices.
- Server results reach the HUD as counters: controllers increment `Completions` and set `Message` and `LastOk`;
  `ForwardMessages` shows each new refusal once as a notice and each confirmation as a chat server line; skill notices
  go to the chat, quest hints to its assistant tab ([chat-and-gm.md](chat-and-gm.md#client-chat-window)).
- Inventory (`HudInventoryController`): left drag moves, Shift-drag splits, drop on another stack merges, drop on an
  equipment slot equips, right click equips/unequips (the slot follows `ItemEquipmentMetadata`; rings take the left
  slot first), drinks a potion or uses a usable item (`HudItemUseController`: the Teleport Core window, confirmations,
  the item-use request). One command in flight; it carries the `InventoryRevision` it was built from.
- Step marks (`GameScene.Terrain.cs`, `PristonCollisionWorld.TryGetIceFloor` / `TryGetWaterHeight`): the stage's ice
  floors are its solid faces of an ice material, its water the faces of a water or see-through (over 0.1) material
  that do not collide. Wading in shallow water (the surface above the feet and less than 32 units up) splashes
  `particle.water` half a unit over the surface at each step 8 units ahead and every 64 frames of 70 per second standing,
  32 walking (8 ahead), 16 running (14 ahead), attacking or casting. Out of the water, each step in an ice field leaves a
  footprint on an ice floor (`image/mk-r.tga` 3 units right and 5 ahead, `mk-l.tga` left), 12 x 12 units, tilted with
  the ice, fading to a fifth over five seconds; at most 20 lie at a time. Local player only, as in the original.
- Held items (`GameScene.HeldItems.cs`): the weapon and off-hand glow and the class-up lights of every drawn player
  ([effects.md](effects.md#held-item-glow)).
- Wing warp map (`HudWingGate`): `WG_map.tga` (256 x 256) at (472, 120) with the icon of each stone the carried wing
  reaches, three rings growing and fading from the hovered icon, the help box at (50, 45) with the hovered stone's name
  and `WarpGateDoc` document (the fee table of `Default.sin`, its `%d` filled with the wings' fees, while none is
  hovered), and the cancel button at the map's lower right. A click checks the level and the gold, then asks for
  confirmation before `IPlayChannel.TravelWingGateAsync`; a refusal shows as a notice.
- Restart window (`HudRestartPanel`): opens 100 death-motion frames (about 1.4 s) after the local player falls, centred
  100 pixels above the middle; field, town and exit buttons light up under the pointer with their help lines; the field
  button stays dark with red help where the server's `RestartOffer` says the field restart is closed (nothing in the
  castle field). The town is chosen after 20 s open; a refused or busy answer opens it again, a stored exit quits the
  game. The scene (`GameScene.Restart.cs`) keeps the last offer and sends `RestartAsync`.
- Inventory window (`HudInventoryPanel`): carried/maximum weight centred in x 326..406 at y 471 of the window; items the
  character cannot use (level, attributes or class against `OwnedItemView.Details`, re-checked on every owner state)
  get the original red cell tint, in the bag and in their equipment slot, and the slot under a dragged unusable item
  turns red. The item box (`HudItemDetailTooltip`) lists, one centred label-and-value line each, the name (unique,
  aged or normal colour), attack power, weapon speed, range, critical, defence, attack rating, absorb (with the catalog
  correction, one decimal), block, speed, integrity, the eight resistances, regeneration, added pools, potion storage,
  the requirements (orange when met, red when not), then the class specialization header and its bonuses in green, the
  shop price and the action hint. An aged copy shows its gauge under the name: the bar (`Aging/Bar.bmp`) filling toward
  the goal while it matures (with the live count from `AgingProgress` for worn copies), else the small plate
  (`Aging/Aging.bmp`), with `+level` in black; the stats its aging raises are light blue (119, 200, 254). A mixed copy's
  name is cyan (150, 255, 255) and the stats its mix raised are blue (110, 165, 250).
- Premium timers (`HudPremiumTimers`) form the column at x 8 under the running-effects bar; usable items have their
  own tooltips (`HudUsableTooltip`). Details: [special-items.md](special-items.md).
- Status window: attribute buttons send `ProgressionCommand` with `ProgressionRevision`; the organic, poison, fire,
  lightning and ice resistance boxes show the owner's resistances.
- Block feedback (`HudStringEffect`): when the player blocks a blow, the original `block.tga` word shows at (354, 488)
  of the 800x600 frame, opaque for 26 interface frames at 70 per second, then fades by 10 alpha per frame while it
  grows 3 pixels and drifts up-left.
- Overweight: while the owner state reports more carried weight than the limit, `GameScene` only walks (run toggles,
  Shift and double clicks walk), matching the server.
- NPC dialog (`HudNpcPanel`): name, the NPC's line, one button per offered service (grey when `Available` is false;
  quest roles get none); skill masters get the skill master box, whose class-change button works when the quests offer
  a rank-up; teleporter destinations replace the buttons. An NPC whose only working service is a shop, warehouse or the aging
  opens it directly (`DirectService`); the aging NPC also keeps its quest role (the rank-up brew). An NPC with a quest role also gets a quest `Talk` (`HudQuestController`); a quest
  dialog it answers with takes the help box, and its confirmation box is modal.
- `HudNpcServiceController`: one request in flight; `Open` replies set the open service and its state; `OutOfReach`,
  `NotOpen` and `Conflict` close it. While the shop is open, an inventory revision change triggers a quote refresh.
- Shop (`HudShopWindow`): buy, sell (click or drop an inventory item on the shop), repair, repair all; opens next to the
  inventory. Stock fills pages in authored order, each item at the first free place down the columns from the left; an
  item that does not fit starts the next page. Warehouse (`HudWarehouseWindow`): grid drag/drop between warehouse and inventory sends Deposit, Move and
  Withdraw with the warehouse revision. Aging (`HudAgingWindow`, layout `HudCraftLayout`, art `HudCraftArt`): the shop
  frame with the aging title and the work panel; an inventory item dropped on it goes to the item box (refused with
  "Item can't be aged"), a sheltom to a cell whose grey hint asks it, a stone or ore to the stone cell. Placed items stay
  in the bag, dimmed and fixed; a right click gives one back. The plate shows `(level + 1) * price / 2`; the start button
  lights when every asked sheltom is in place and sends `Age` with the materials. The result shows the original message
  (+1, +2, -1, -2 or failed) with the drink sound or `sheltom-failure.wav`, and the window closes as the original's does.
  Mixing (`HudMixWindow`): the same frame with the mixing title and panel, or the alchemist's panel and title; an item
  in the item box (mixing only), sheltoms or seals in the twelve cells (alchemy: sheltoms up to the eleventh), the respec
  stone in the stone cell. The plate shows the item's price or the alchemy prices, both scaled by the offer's
  `PricePercent`; the result shows what the mix added (the formula's lines), the reconstruction, the refining or the
  failure.
  Server rules: [application.md](application.md), [world.md](world.md).
- Target frame (`HudCombat.DrawTarget`, `TargetSelection`): a clicked monster or NPC owns the frame; otherwise the
  hovered entity. The frame stays after an attack is cancelled and closes when the entity is removed or has been dead
  for 0x50 frames at 70 Hz. With `classicTargetHealthBar` false (default) the bar shows with level and life
  percentage; when true the bar shows without numbers, only after the local player started attacking that monster at
  full life.
- 3D portrait (`HudTargetPortrait`): rendered each frame into its own 4x MSAA target through
  `PristonRenderer.DrawActorPortrait`, framed on the `Bip01 Head` bone (fallback: model height) plus the authored
  portrait offsets from `MonsterRenderInfo`/`NpcRenderInfo`, with its own studio lighting.
- Settings (`HudSettingsPanel`, system menu entry Settings) edits `PresentationSettings.Requested`;
  `PresentationController` applies it at the next frame boundary. Display changes require confirmation within 15 s
  (`DisplayConfirmation`), otherwise they revert.
- Party, quest and system menu entries other than Settings are drawn without behaviour.

## Audio

- `PristonAudioLibrary.LoadBootstrap` decodes every sound at startup through `PristonWaveDecoder`: opening and
  character-select music (`StartImage/...`), field music by BGM code (`wav/BGM`, `wav/Sod/BGM`), field ambience loops
  and positional ambience (`wav/Ambient`), NPC motion banks per sound code, and hashed NPC talk lines
  (`wav/Talk/<code>.wav`; optional, playback disabled when absent).
- `TalkSound.GetSoundCode` hashes the CP949 bytes of an NPC line (ASCII letters uppercased) into the talk file
  code.
- `FieldAudioController.SelectField` picks music by `FieldDef.BackMusicCode` (3.6 s fade); field ambience depends on the
  field state and, in forests, the time of day; positional ambience uses the original distance falloff.
- `NpcAudioController` plays the dialog line or a motion sound of the NPC at its position relative to the listener.
- `MusicEnabled`/`SoundEnabled` come from client settings each frame (`ApplySettings`).
- Skill sounds: `SkillSoundBank` ([skills.md](skills.md)).
- Character sounds (`CombatSounds`): `CharacterSoundBanks` maps every character sound code to its folder (monsters and
  summons by sound code, NPCs, player classes 1 to 10 by class code); a file plays for the motion its name starts with
  (`attack`, `damage`, `dead`, `neutral`, `move`, `disappear`/`change`, `skill`, `hammer`). A folder is read when its
  character first appears and decoded on a worker through a `SkillSoundBank`; a sound not ready yet is skipped.
  Monsters drawn smaller than their model play higher (3600/2205 of the rate), more than twice as large lower
  (1400/2205).
- `MonsterSoundCues` follows each monster's animation like `MonsterPresentation`: the attack or skill sound on the
  clip's first two event frames, the damage, death or vanishing sound when the monster enters that motion, and the idle
  sound one time in four when it stops.
- Players: a server `Hit` or `Miss` of a player's swing plays the weapon sound of the attacker's weapon (`CombatSounds.
  WeaponCode`: axes, swords with the first three as small blades, spears, claws, bows, staves cast by magicians and
  priestesses and struck by others, hammers, throwing weapons, punches without a weapon; one- or two-handed by
  `ItemEquipmentMetadata.IsTwoHanded`; a miss plays the hand's miss sound, a critical adds the critical sound). Damage
  and death play the class's sound, a `Revive` the restart chime. The local player's walk and run clips play the floor's
  footsteps (stone in towns and ruins, dungeon stone, soft ground in forests and deserts) on their first two event
  frames. Everything comes from the presence stream, so every client near the action plays it.
- Equipment looks (`EquipmentVisuals`, `EquipmentVisualTables`): every item with a model has its motion index (the
  weapon index the character motion files list; items newer than those files take the first item of their family and
  hand class), model (`image/sinimage/Items/DropItem/it<model>.smd`, also its ground model), hand and village bone. An
  armour or robe wears the body of its catalog ordinal (`ArmorSlots`: ordinals from 26 sit sixteen slots further, so
  DA126-129 wear the 25th-28th bodies, DA301/401 the 29th, DA302-304/402-404 the 30th-32nd and DA131+ the costume
  bodies) from its class row (A, B, C, D, mA, mB, mC, mD, E, mE; 75 slots each); premium costumes (`CA`) keep the class
  body and wear wings.
- Progression (`GameScene.Progression`): a `LevelUp` combat event plays `vfx.player.levelup` (with the level-up chime) at
  the player, a `SkillLearned` event the skill-learn light (`ItemVfx.Learn`) and sound, for every client near.
- Item sounds (`ItemSoundRules`, `image/Sinimage/Sound`): an item sounds by catalog family (three characters of its key,
  then two: axes, claws, hammers, staves, spears, bows, swords, throwing weapons, armour, boots, gloves, shields,
  amulets, armlets, rings, sheltoms, potions...) with per-item exceptions; gold rings the coin. They play for the local
  player only: a ground pickup the server accepted (`GroundLootInteraction`), lifting an item in the pack, a successful
  move, split, merge, equip or unequip (`HudInventoryController.ItemSound`), and a shop buy or sell or a warehouse
  deposit, withdrawal or move (`HudNpcServiceController.Succeeded`).

## How to

### Add a HUD window

1. Create the window class in `UI/Widgets` (or `UI/Npc` for an NPC service, `UI/Inventory` for item views) with static
   layout rectangles in 800x600 design units, `Update`/`Click` for input and `Draw(HudPainter, ...)`.
2. Own it in `GameplayHud`: request its sprite paths through `HudSprites.Request` (add them to `HudArt` or
   `HudNpcArt` if they are fixed), route keys in `UpdateKeys`, clicks in `Click`, pointer capture in `Captures`, closing
   in `ClosePanel`, and draw it at the right place in `Draw`. If it shares the side area, open it through a `HudSlide`
   and `OpenOnly`.
3. Server calls go through a delegate passed into `GameplayHud` (as `applyInventory`, `requestNpcService`) that
   `GameplayScreen.Enter` wires to a `GameScene` method calling `IPlayChannel`. Keep one request in flight, poll the
   task in `Update`, report through `Completions`/`Message`.
4. Add layout or hit-test cases to `tests/PristonTale.Tests/HudInteractionTests.cs`; use `HudCaptureProbe`
   (`--capture-hud`) to check the picture. Update this document's HUD section.

### Add a screen

1. Derive from `GameScreen` in `Screens/<Name>/`; take dependencies from `ScreenContext`; load in `Enter`, release GPU
   resources and cancel backend calls in `Dispose`.
2. Start backend calls as tasks and poll them in `Update` (see `CharacterSelectScreen.PollBusy`).
3. Transition with `Context.RequestScreen(new NextScreen(Context, ...))`; override `HandleEscape` if Escape must not
   quit; override `StatusText` for the window title.
4. Skip input while `Context.Presentation.AwaitingConfirmation` is set.

### Handle a new server message on the client

1. Define the opcode and packet in `Shared/PristonTale.Protocol` ([protocol.md](protocol.md)) and its encoder on the
   server ([server.md](server.md)).
2. Stream data (world state, events): add a case to `PresenceDecoder.TryHandle` (both the `switch` and the opcode list
   in its return expression), convert to neutral types in `Client.Net/Presence`, and add an `init` property to
   `PresenceUpdate`. Remap coordinates in `GameScene.RemapToWorld` if the property carries positions consumed in world
   space, or flip Z in the consumer. Consume it in `GameScene.DrainPresence` or through `PresenceObserved`.
3. Request/reply data: add a case to `GameServerConnection.HandleAsync` that completes a pending
   `TaskCompletionSource` keyed by operation id (or collects state on the pending `ServiceRequest`), expose it through
   `IPlayChannel`/`OnlinePlayChannel` (await `_start` first) and fail it in `RunAsync`'s `finally` when the connection
   ends.
4. Add a wire round trip to `tests/PristonTale.Tests/RemotePlaybackTests.cs` or `ClientTests.cs` (`WireRoundTrip` runs
   the server encoder and the client decoder over real packet bytes); register new case classes in `Program.cs`.

### Load a new kind of asset in game

Prepare it off-thread (`Task.Run` plus a renderer `Prepare*Async` for textures) and publish it from `Update` within a
budget; never read files in `Update`/`Draw` after `BeginRemotePresence`. Resolve paths with `PristonFileResolver`.

## Pitfalls

- Spawns, states and warps are in world `(x, -z)` after `RemapToWorld`; combat events, skill events, ground items and
  the update passed to `PresenceObserved` (so `HudCombat` positions and the minimap NPC list) stay in transport
  coordinates.
- `GameScene` planar vectors are `(x, worldZ)`, not `(x, ptZ)`; `EnteredCharacter.SpawnZ` and field gate coordinates
  are PT-global and are negated when placed.
- Self state echoes intentionally do not move the local avatar; adding a position correction from them causes rubber
  banding. Use a forced `FieldWarp` on the server for corrections.
- `RenderPortrait` must run before the world draw: binding its render target discards the back buffer contents.
- The presence channels are bounded; if the game thread stops draining (more than 64 updates per frame sustained), the
  receive loop's `TryWrite` fails and the connection ends.
- `WorldRenderCatalog` is filled on the receive thread; a template is registered before any spawn that references it is
  published, but spawns that arrived before `SetCatalog` reached the scene wait in `_pendingModels`.
- `GameplayHud.Update` has an `internal` overload taking keyboard and mouse state for tests; the public one reads the
  devices.
- HUD keys `I`, `V`, `C`, `S`, `X`, `Q` act on any press without Ctrl unless the chat input has focus
  (`GameplayHud.KeyboardCaptured`); a new key handler must check it too.
- Quest texts load on the thread pool; `GameplayHud.IconsIdle` waits for them so captures show the text.
- `IPlayChannel.SendSkillCommandAsync` has a default no-op implementation; test doubles inherit it silently.
- `PresentationController.Pace`, called from `Game1.EndDraw`, sleeps on the game thread by design (frame limiter after
  present); do not move frame work after it.

## Related

- [architecture.md](architecture.md) — process and threading model.
- [rendering.md](rendering.md) — `PristonRenderer`, stage upload, actors, portrait drawing, camera.
- [shaders.md](shaders.md) — content build of the shader library.
- [skills.md](skills.md) — skill sockets, casting, skill timeline, cooldowns, buffs.
- [effects.md](effects.md) — effects engine and VFX data.
- [client-settings.md](client-settings.md) — `client-settings.json` fields and presets.
- [protocol.md](protocol.md) — opcodes, packets, framing and TLS.
- [application.md](application.md), [server.md](server.md) — the authority behind each request.
- [testing.md](testing.md) — regression runner and capture probes.
