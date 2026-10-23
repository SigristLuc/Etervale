# Game server

The headless game server host in `Server/PristonTale.Server`: process startup, dependency injection, configuration, the
TCP edge in `Net/` and the world tick driver. Game rules live in `Shared/PristonTale.Application` and below; wire
layouts live in [protocol.md](protocol.md).

## Read this when

- The server does not start (migrations, empty catalogs, missing stages, `Assets:Root`, database provider).
- You add or change a server setting, or need a default for one.
- You handle a new client packet, or change how a packet maps to an application call and back.
- You change connection limits, login throttling, timeouts or log sampling.
- You register a new service or hosted service in the server's DI container.
- You run the server against PostgreSQL, a copied database or a load test.

## Code map

| Path | Responsibility |
| --- | --- |
| [Program.cs](../Server/PristonTale.Server/Program.cs) | Generic host: `Host.CreateApplicationBuilder(args)`, `AddPristonServer()`, `Run()`. No HTTP endpoints. |
| [Hosting/HostingExtensions.cs](../Server/PristonTale.Server/Hosting/HostingExtensions.cs) | `AddPristonServer`: every DI registration, provider selection, `WorldServerOptions`, `DefaultGamePort` (10009, the default of `Network:Port`). |
| `Hosting/DatabaseMigrationService.cs` | Hosted service: `IDatabaseMigrator.MigrateAsync` on start. |
| `Hosting/WorldDataLoadService.cs` | Hosted service: `WorldDataAppService.BuildAsync` into `WorldDataHolder.Current`; logs catalog counts. |
| `Hosting/FieldTerrainLoadService.cs` | Hosted service: reads every field's stage `.smd` collision in parallel into `FieldTerrainCatalog`. |
| `World/WorldTickService.cs` | `BackgroundService` calling `WorldServer.Tick(1/20 s, tick)` on a `PeriodicTimer`; logs tick percentiles. |
| `World/QuestUpkeepService.cs` | `BackgroundService` reading `WorldServer.QuestUpkeepRequests` and running `QuestAppService.RunUpkeepAsync`, at most 8 players at a time ([quests.md](quests.md)). |
| [Net/PristonTcpServer.cs](../Server/PristonTale.Server/Net/PristonTcpServer.cs) | Listener, per-address limit, one `ClientSession` per socket, housekeeping (idle/login timeouts, bandwidth log). |
| [Net/ClientSession.cs](../Server/PristonTale.Server/Net/ClientSession.cs) | Per-connection state machine: packet dispatch to application services and replies. |
| `Net/ClientSession.Chat.cs` | The session's `IChatLink`: chat lines, forced warps, close requests and kicks from game masters. |
| `Net/GameServerControl.cs` | `IServerOperations` for game masters: user limit, blocked logins, the shutdown countdown and host stop. |
| `Net/PresenceEncoder.cs` | Turns the receiver's `WorldSnapshot` into presence packets (play data, events, drops, owner state). |
| `Net/WireMapping.cs`, `Net/NpcWireMapping.cs`, `Net/QuestWireMapping.cs` | Authoritative views to wire structs (`CharacterInfo`, play data, events, NPC service state, quest messages). |
| `Net/NetworkLimits.cs` | `Network:*` settings with defaults and validation; IPv4-mapped address normalisation. |
| `Net/ConnectionGuards.cs` | `PacketRateLimiter`, `TokenBucket`, `ConnectionLimiter`, `LoginThrottle`, `LogSampler`. |
| `Application/README.md` | Placeholder rule: server-only orchestration goes here; use cases stay in `Shared/PristonTale.Application`. |
| `appsettings.json` | Logging level, PostgreSQL connection string, `Database:Provider` and `Database:SqlitePath`. |

The project uses `Microsoft.NET.Sdk.Web` for its hosting defaults and exposes internals to `PristonTale.Tests`.

## Startup sequence

`AddPristonServer` runs while the builder is configured; it throws immediately for an unknown `Database:Provider` or a
missing PostgreSQL connection string. The host then constructs every hosted service and starts them one after another in
registration order. An exception from any `StartAsync` stops the host before the next service starts.

| Order | Service | Work | Fails startup when |
| --- | --- | --- | --- |
| 1 | `DatabaseMigrationService` | Applies pending FluentMigrator migrations of the chosen provider. | The database cannot be opened or migrated. |
| 2 | `WorldDataLoadService` | Loads items, monsters, NPCs, field spawns and motion timings from the database. | Repository errors only. An empty, un-imported database loads zero rows and starts an empty world. |
| 3 | `FieldTerrainLoadService` | Loads the collision of all 43 fields of `WorldCatalog.AllFields` (the quest arena included) (`FieldDef.StageSmd`). | No assets directory, or any stage missing or unreadable, unless `Assets:AllowMissingTerrain=true`. |
| 4 | `WorldTickService` | Starts the 20 Hz tick loop. | Never; a throwing tick is logged and the loop continues. |
| 5 | `PristonTcpServer` | Binds `[::]:<Network:Port>` (10009) dual-stack, backlog 512, and accepts. | The port is in use (the host stops). |
| 6 | `QuestUpkeepService` | Stores the quest work the tick queues (quest drops, quest weapon growth and expiry). | Never; a failed upkeep is logged and retried on the field's next check. |

Terrain details:

- Assets root: `Assets:Root` when non-empty, otherwise `AssetPath.FindAssetsRoot()`, which looks for an `assets`
  directory in the working directory and its ancestors, then in the executable directory and its ancestors.
- Stage paths are resolved case-insensitively with either separator (`AssetPath.Resolve`), parsed with
  `StageCollisionReader.Read` and published once with `FieldTerrainCatalog.Publish`, keyed by field code.
- With `Assets:AllowMissingTerrain=true`, failures become a warning and those fields use `FieldTerrain.None` (no floor
  or wall checks). Use it only for tests and development.
- A full load logs `Field terrain loaded: 43/43 stages, ...` with about 1.93 million collision triangles and 179 MiB,
  well under a second.

`WorldServer` creates `FieldInstance`s lazily on first use and reads `WorldDataHolder.Current` and the terrain catalog
at that moment, so both loaders must finish before the listener accepts players; the sequential start guarantees that.

## DI composition

All registrations are singletons in `HostingExtensions.AddPristonServer`.

| Service | Implementation / factory | Notes |
| --- | --- | --- |
| `ISessionRegistry` | `SessionRegistry` | In-memory login sessions keyed by token; allocates player entity serials. |
| `CharacterClassRegistry` | type | Class definitions (`PristonTale.Simulation.Characters`). |
| `AuthAppService`, `CharacterAppService`, `SessionAppService`, `NpcAppService`, `QuestAppService` | types | Use cases called by `ClientSession` (and `QuestUpkeepService`). |
| `WorldCatalog` (`PristonTale.Simulation.World`) | `_ => new WorldCatalog()` | Factory so DI does not pick the collection constructor with an empty field set. |
| Persistence | `AddPristonSqlite(path)` or `AddPristonPostgres(connectionString)` | Repositories, `IDbConnectionFactory`, `IDatabaseMigrator`, FluentMigrator runner. See [persistence.md](persistence.md). |
| `WorldDataAppService`, `WorldDataHolder` | types | Catalog build and the swap-in holder read by the world and the encoders. |
| `FieldTerrainCatalog` | type | Filled by `FieldTerrainLoadService`. |
| `WorldServer` | factory with `WorldServerOptions` from `World:*` | Options are validated at registration (`WorldServerOptions.Validated`). |
| `IServerStatusReadModel` | `WorldStatusReadModel` | Registered for status queries; nothing in the server consumes it. |

Hosted services are registered in the order of the startup table.

## Database provider

| Provider | Selection | Connection |
| --- | --- | --- |
| SQLite (default) | `Database:Provider` = `Sqlite` (case-insensitive) or unset | `Database:SqlitePath`, default `data/pristontale.db`. |
| PostgreSQL | `Database:Provider` = `Postgres` | `ConnectionStrings:Postgres`; `appsettings.json` points at the `docker-compose.yml` instance on port 5433. |

- A relative SQLite path is resolved by `SqliteDatabasePath.Resolve` against the first ancestor of the executable
  directory that contains `PristonTale.sln`, else against the executable directory. It does not depend on the working
  directory. The directory is created if missing.
- SQLite runs with WAL journaling and `synchronous=NORMAL`; `-wal` and `-shm` files sit next to the database.
- Environment form: `Database__Provider=Postgres`, `ConnectionStrings__Postgres="Host=...;Port=5433;..."`.
- Start PostgreSQL with `docker compose up -d --wait`. `docker compose down` keeps the named volume; `down -v` deletes
  it.
- The server migrates the schema itself but never imports world data. Import with `tools/PristonTale.WorldImport`
  ([world-import.md](world-import.md)) and create accounts with `tools/PristonTale.DevAccounts` ([tools.md](tools.md)).

## Settings

Sources, later ones winning: `appsettings.json`, `appsettings.{Environment}.json` (none exist), environment variables
(`__` for `:`, e.g. `World__ExperienceMultiplier=1`), command line (`--World:ExperienceMultiplier=1`). A `Network:*`
limit of `0` disables that limit; negative values stop startup.

| Key | Default | Read by | Meaning |
| --- | --- | --- | --- |
| `Database:Provider` | `Sqlite` | `HostingExtensions` | `Sqlite` or `Postgres`; anything else throws. |
| `Database:SqlitePath` | `data/pristontale.db` | `HostingExtensions` | SQLite file; relative to the solution root (see above). |
| `ConnectionStrings:Postgres` | local compose string | `HostingExtensions` | Required when the provider is `Postgres`. |
| `Assets:Root` | auto-detected `assets` | `FieldTerrainLoadService` | Directory holding the original asset tree with the field stages. |
| `Assets:AllowMissingTerrain` | `false` | `FieldTerrainLoadService` | Start without an assets directory or with missing/unreadable stages. |
| `World:ExperienceMultiplier` | 5 | `WorldServerOptions` | Server-wide bonus on monster experience; must be at least 1. |
| `World:MaxPendingCommandsPerPlayer` | 64 | `WorldServerOptions` | Commands one player may have waiting for the tick; at least 1. |
| `Network:Port` | 10009 | `PristonTcpServer` | TCP game port, 1 to 65535; the client always connects to 10009. |
| `Network:PacketLimit` | 251 | `NetworkLimits` | Packets per window before the connection closes (`GETPLAYERINFO` excluded). |
| `Network:PacketWindowSeconds` | 5 | `NetworkLimits` | Fixed window of the packet limit. |
| `Network:MaxConnectionsPerIp` | 8 | `NetworkLimits` | Open connections per remote address; extra sockets are closed on accept. |
| `Network:LoginTimeoutSeconds` | 30 | `NetworkLimits` | Close connections that have not logged in; checked every 5 s. |
| `Network:LoginFailuresPerAccount` | 5 | `NetworkLimits` | Bad-password logins per account per window before refusal. |
| `Network:LoginFailuresPerIp` | 20 | `NetworkLimits` | Bad-password logins per address per window before refusal. |
| `Network:LoginFailureWindowSeconds` | 300 | `NetworkLimits` | Window of both failure counters. |
| `Network:PlayerInfoBurst` | 512 | `NetworkLimits` | Token-bucket capacity for `GETPLAYERINFO` requests. |
| `Network:PlayerInfoPerSecond` | 128 | `NetworkLimits` | Token refill rate for `GETPLAYERINFO`. |
| `GameMasters:TestServer` | `false` | `GameMasterOptions` | Lets every game master use `/GiveMeExp!` and `/GiveMeMoney!`. |
| `GameMasters:DebugServer` | `false` | `GameMasterOptions` | As `TestServer`, and `/` with an item code drops that item. |
| `GameMasters:PotionMonitor` | `true` | `GameMasterOptions` | Enables `/get_potion`. |
| `GameMasters:ServerCode` | 0 | `GameMasterOptions` | Server number that prefixes the game-master information replies. |
| `Logging:LogLevel:Default` | `Information` | host | Standard .NET logging filter. |

Fixed values that are not settings: idle timeout 60 s, housekeeping period 5 s, report period 30 s,
tick rate 20 Hz, send backlog limit 512 KiB per connection (`PristonConnectionOptions.MaxPendingSendBytes`).

## TCP edge

### Connection lifecycle

1. `PristonTcpServer` accepts, normalises IPv4-mapped addresses (`NetworkLimits.AddressOf`) and calls
   `ConnectionLimiter.TryAcquire`; a refused socket is closed without a reply.
2. A `ClientSession` wraps a `PristonConnection` (framing, cipher, replay window, coalesced writer; see
   [protocol.md](protocol.md#transport)) and sends the encrypted `VERSION` command (`WParam` = 167).
3. `PristonConnection.RunAsync` reads frames and awaits `ClientSession.HandleAsync` for each packet before reading the
   next: one connection's packets are processed strictly in order, and a slow database call delays that connection only.
4. Housekeeping (every 5 s): closes sockets silent for 60 s (`idle timeout`) and unauthenticated sockets older than the
   login timeout (`login timeout`; heartbeats do not extend it), prunes the login throttle and adds up traffic.
5. On close: `ClosePlayAsync` leaves the world and saves the character, the session token is removed from the registry,
   the per-address slot is released and the connection is disposed.
6. On shutdown every connection is closed with `server stopping` and awaited.

### Inbound packet mapping

`ClientSession.HandleAsync` switches on the decoded code. Encryption is not required per opcode; both envelopes reach
the same switch.

| Packet | Handler | Application call | Reply |
| --- | --- | --- | --- |
| `CHECK_NETSTATE` 1-5 | inline | `ChatAppService.Heartbeat` (the standing notice) | Echo with `SParam` = server `Environment.TickCount`. |
| `CHATMESSAGE` | `ChatAsync` | `ChatAppService.SayAsync` (spoken line, player or game-master command) | Chat lines to the hearers ([chat-and-gm.md](chat-and-gm.md)). |
| `PARTY_CHATMSG` | `ChatAsync` | `ChatAppService.PartyAsync` | Party line to the other party members. |
| `ID_GETUSERINFO` | `LoginAsync` | `LoginThrottle.IsBlocked`, `AuthAppService.LoginAsync`, `ISessionRegistry.TryGet` | `ID_SETUSERINFO` + `SERVER_INFO`, or `FAILCONNECT` (`WParam` code, `EParam` 2). |
| `ISRECORDDATA` | inline | `CharacterAppService.NameExistsAsync` | Echo, `WParam` 1 when the name exists. |
| `INSRECORDDATA` | `CreateCharacterAsync` | `CharacterAppService.CreateCharacterAsync(token, name, LParam class, SParam face)` | New list on success, then echo. |
| `DELRECORDDATA` | `DeleteCharacterAsync` | `CharacterAppService.DeleteCharacterAsync` | New list on success, then echo. |
| `GETRECORDDATA` | `EnterWorldAsync` | `SessionAppService.EnterWorldAsync` | `RECORDDATA` chunks, or `FAILCONNECT` (`LParam` `EnterWorldFailure`, `EParam` 3). |
| `PLAYERINFO` | `JoinWorldAsync` | `PlayerSession.TryBeginPlay`, `WorldPlayerFactory.Create`, `WorldServer.JoinAsync` | `CONNECTED`; starts the presence pump. |
| `TRANSPLAYDATA` | `ReportMovement` | `WorldServer.MoveToFieldAsync` or `ReportMovementAsync` | `WARPFIELD` correction when rejected. |
| `ATTACK_DAMAGE` | `AttackAsync` | `WorldServer.InteractNpcAsync` (target is a known NPC) or `SetAttackTargetAsync` | `NpcResult`, or nothing (results arrive as presence). |
| `GETPLAYERINFO` | `DescribeObject` | `PresenceEncoder.Describe` | Plaintext `PLAYERINFO` when the serial is in view. |
| `RECORDDATA` | `AcknowledgeClientSave` | none (discarded) | `RECORDRESULT` `WParam` 1 after the last chunk. |
| `InventoryCommand` | `ApplyInventoryAsync` | `SessionAppService.ApplyInventoryAsync` | `InventoryResult`. |
| `ProgressionCommand` | `ApplyProgressionAsync` | `SessionAppService.ApplyProgressionAsync` | `ProgressionResult`. |
| `NpcServiceRequest` | `RequestNpcServiceAsync` | `NpcAppService.RequestAsync` | State messages, `NpcServiceResult`, and `WARPFIELD` (`EParam` 1) for a teleport. |
| `QuestRequest` | `RequestQuestAsync` | `QuestAppService.RequestAsync` | `QuestResult`; travel to or from the arena arrives as the encoder's `WARPFIELD`. |
| `SkillCast` | `CastSkillAsync` | `WorldServer.CastSkillAsync` | `SkillCastResult`. |
| `LeaveWorld` | inline | `ClosePlayAsync` (leave, save) | `LeaveWorld` echo, `WParam` 1. |
| anything else | ignored | none | none |

Error handling inside `HandleAsync`:

| Exception | Effect |
| --- | --- |
| `InvalidSessionException`, `InvalidOperationException` (includes `WorldBusyException`) | Packet dropped, sampled `Rejected` warning; the connection stays open. |
| `DbException` | `ReplyStorageFailure`: login gets `FAILCONNECT` -16, record commands get an echo with `WParam` 0 and `CreateCharacterFailure.Rejected`, `GETRECORDDATA` gets `FAILCONNECT` with `EnterWorldFailure.Rejected`. |
| `InvalidDataException` (malformed packet from a `Read`) | Escapes to `PristonConnection`, which closes with `protocol: ...`. `ATTACK_DAMAGE` decoding errors are caught and only logged. |
| anything else | Ends the session; logged as `Client session ... failed`. |

Inventory, progression, NPC service and quest handlers convert session, operation and storage exceptions into a failed
result packet instead, so the client's pending operation always completes.

### Outbound presence

After `CONNECTED`, `PumpAsync` reads the player's `WorldOutbox` and calls `PresenceEncoder.Encode` per snapshot on a
thread-pool task, separate from the receive loop. Per snapshot, in order: `WARPFIELD` on a field change (after removing
everything known), `PresenceClock` before the first timeline packet, `PLAYDATAGROUP` (one or more), `RemoveObjects`,
`CombatEvents`, `SkillEvents`, `StatusEffects`, `PLAYITEM`/`DELITEM`, `OwnerStatus` with `Inventory` parts and `Skills`
when their revisions changed, `SkillCooldowns`, then `QuestJournal` and `QuestNotices` from the snapshot's quest update.
In the quest arena (field 32) the encoder leaves out other players and monsters of other players' duels. View ranges,
rates and the movement budget are in [protocol.md](protocol.md#server-capacity). A reliable-event overflow in the outbox
(4096 queued events) ends the pump and closes the connection with `presence overflow`; the client must reconnect.

### Movement and corrections

- A report with `wArea[0]` different from the session field goes to `MoveToFieldAsync`; a refusal or a full queue sends
  a forced `WARPFIELD` back to the encoder's last known self position.
- Other reports go to `ReportMovementAsync`; `MovementVerdict.Rejected` sends `WARPFIELD` with `EParam` 1, at most once
  per 300 ms. A full queue silently drops the report; the next report carries a newer position.
- Both continuations run on the thread pool; `ClientSession` serialises its own scratch buffer with `_sendGate`.

### Limits and abuse handling

| Guard | Scope | Behaviour |
| --- | --- | --- |
| `ConnectionLimiter` | per remote address, process-wide | Refuses sockets above `MaxConnectionsPerIp`; released when the session ends. |
| `PacketRateLimiter` | per connection, fixed window | Counts every decoded packet except `GETPLAYERINFO`; exceeding it closes with `packet flood`. Replayed encrypted packets are dropped before counting. |
| `TokenBucket` | per connection | `GETPLAYERINFO` beyond the bucket is dropped (a 32-byte request costs a 504-byte reply). |
| `LoginThrottle` | per account (case-insensitive) and per address | Only `BadCredentials` counts. A blocked attempt gets `FAILCONNECT` -16 before the password hash is computed. Success clears the account counter only. At most 65536 keys per map. |
| Login timeout | per connection | Unauthenticated sockets close after `LoginTimeoutSeconds`. |
| Second login | per connection | `ID_GETUSERINFO` after a successful login gets `FAILCONNECT` -4. |
| World quota | per player | `WorldServer` refuses more than `MaxPendingCommandsPerPlayer` queued commands with `WorldBusyException`; the shared queue holds 65536. |
| Send backlog | per connection | More than 512 KiB unsent closes the connection (`send backlog exceeded ...`). |

`FAILCONNECT` login codes: -2 bad credentials (unknown account, wrong or empty password), -4 already logged in on this
connection, -16 busy (throttled or storage failure), -24 account blocked.

### Logging

| Line | Period |
| --- | --- |
| `World tick: N players, p50, p99, max over N ticks (budget 50 ms)` | 30 s |
| `TCP: N connections, N in world, out/in KiB/s` | 30 s (checked every 5 s) |
| `Refused connection from ...` | at most one per 5 s, with the count of suppressed lines |
| `Closed ...: reason` | at most one per 1 s; normal reasons are never logged |
| `Rejected <code> from ...: reason` | at most one per 5 s across all connections |

Normal close reasons: `peer closed`, `receive ended`, `cancelled`, `server stopping`, `disposed`. Everything else (idle
or login timeout, packet flood, protocol error, send backlog, presence overflow, socket errors) is sampled.

## How to

- **Add a setting.** Read it in `HostingExtensions` (world, provider) or `NetworkLimits.From` (network, via `Read` so
  negatives fail), give it a default constant in the options type, validate it, add it to the settings table here and to
  `NetworkHardeningTests` (`NetworkConfigurationAsync` / `WorldOptionsAsync`).
- **Handle a new client packet.** Define the packet and opcode
  ([protocol.md](protocol.md#how-to-add-an-opcode-or-packet)), add a `case` in `ClientSession.HandleAsync`, convert it
  into an application input (`Shared/PristonTale.Application`), and reply with `SendLarge(packet.Write, Size, encrypt:
  true)`. Catch the exceptions the call can throw and answer with a failed result when the client waits for one. Update
  the inbound table above.
- **Send new tick-driven state.** Add it to `WorldSnapshot`/`OwnerState` in the application layer, map it in
  `WireMapping`, emit it in `PresenceEncoder.EncodeLocked` (call `SendClock` first if it belongs on the timeline),
  decode it in the client's `PresenceDecoder`, and cover it with a `WireRoundTrip` test.
- **Register a startup step.** Add an `IHostedService` in `AddPristonServer` at the right position: anything the world
  reads must be registered before `WorldTickService` and `PristonTcpServer`. Throw from `StartAsync` to stop startup.
- **Run against a copy or Postgres.** `--Database:SqlitePath=<copy.db>` or `Database__Provider=Postgres` with
  `ConnectionStrings__Postgres`; for local load tests add `--Network:MaxConnectionsPerIp=0`.

## Pitfalls

- The port is a constant (`HostingExtensions.DefaultGamePort`); there is no setting for it. `SERVER_INFO` advertises the
  login socket's local IPv4 address and port 10009. The Etervale client ignores the address and keeps its socket.
- A relative `Database:SqlitePath` is not relative to the working directory but to the solution root found from the
  binary. `DevAccounts` resolves `--sqlite` the same way; `WorldImport` uses its `--sqlite` path as given (relative to
  the working directory). Run the tools from the repository root so all three open the same file.
- An un-imported database starts cleanly with no monsters or NPCs; check the `World catalogs loaded` line.
- `Assets:AllowMissingTerrain` leaves the affected fields on `FieldTerrain.None`: no floor or wall checks for movement
  reports, and positions keep the heights they are given.
- `HandleAsync` awaits application calls in the receive loop; never block there on the tick. World calls return tasks
  that complete inside the tick, so movement and attacks are fire-and-forget with continuations.
- `WorldBusyException` is thrown synchronously by `WorldServer` methods, not through the returned task; callers must
  wrap the call itself, as `ReportMovement` and `CastSkillAsync` do.
- An NPC click is recognised only when the `ATTACK_DAMAGE` target is an NPC the receiver's `PresenceEncoder` currently
  knows; an unknown serial becomes an attack target change.
- Client-sent `RECORDDATA` is acknowledged and discarded: the server owns the record. `ClientSession` saves the
  character on `LeaveWorld` and on disconnect (`SessionAppService.SaveCharacterAsync`); inventory, progression and NPC
  transactions persist their own changes.

## Related

- [protocol.md](protocol.md): wire formats, opcodes, presence timeline, capacity numbers.
- [application.md](application.md): `WorldServer`, sessions, transactions and the tick.
- [persistence.md](persistence.md): providers, migrations, repositories.
- [world-import.md](world-import.md): filling the catalogs the server loads.
- [architecture.md](architecture.md): process and threading model.
- [testing.md](testing.md): regression runner, smoke client and load test.
- [tools.md](tools.md): `DevAccounts`, `SmokeClient`, `LoadTest`.
