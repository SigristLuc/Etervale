# Network protocol

Etervale is online only. The client and server speak the original Priston Tale TCP protocol of the English C++ client:
the same framing, cipher, opcodes and 32-bit x86 struct layouts. The server stays
authoritative, so a few messages keep the original layout but change meaning, and operations the original client
performed locally use Etervale extension opcodes. The codecs live in this project; the server edge is
[`Server/PristonTale.Server/Net`](../../Server/PristonTale.Server/Net) and the client edge is
[`Client/PristonTale.Client.Net`](../../Client/PristonTale.Client.Net).

## Transport

| Item | Value |
| --- | --- |
| Port | TCP 10009 (English build) |
| Frame | `int32 size` (including the header), `uint32 code`, at most 8192 bytes |
| Cipher | encrypted envelope: XOR `0xED`, running key, 16-bit byte-sum checksum, code `0x8001xxxx` |
| Encrypted limit | packets of 8176 bytes or more go out in plaintext, as in the original |
| Replay check | counter starts at `0x0010`; repeats of the last 16 and counters 16 behind the highest are dropped |

`PristonConnection` reads into a fixed buffer, decodes encrypted frames, and hands each packet to a handler before reading
the next one. Sends from any thread append to one pending buffer that a single writer flushes, so all packets queued
during one socket write leave together. A peer whose pending bytes exceed the limit (512 KiB) is disconnected; the
original dropped packets once its global send queue was full.

Deviations from the original transport:

- The second envelope (`0x9001xxxx`) is rejected. The original only used it with a decoder that the server pushes to the
  client as x86 machine code.
- The replay window compares counters modulo 65536. The original compares plain integers and stops accepting
  encrypted packets after about 65,520 of them in one direction.

## Login, character select and world entry

| Step | Packet | Notes |
| --- | --- | --- |
| Connect | S→C `VERSION` | `WParam` = minimum client version (167); the client sends its own version 3122 later. |
| Log in | C→S `ID_GETUSERINFO` (account command packet, 84 B) | `szId` = account, `szName` = password, as in the original. |
| Success | S→C `ID_SETUSERINFO` (1484 B) and `SERVER_INFO` | Character list and the one game server at this address. |
| Failure | S→C `FAILCONNECT` | `WParam` uses the original codes: -2 bad credentials, -4 already logged in, -16 storage busy, -24 denied. |
| Name check | `ISRECORDDATA` echo | `WParam` = 1 when the name exists. |
| Create | `INSRECORDDATA` echo | Request `LParam` = class code, `SParam` = face (unset in the original). Echo `WParam` = 1/0, `LParam` = reason. |
| Delete | `DELRECORDDATA` echo | The original sends no reply; Etervale echoes the result. |
| Select | C→S `GETRECORDDATA` (52 B) | `SParam` = client version. |
| Record | S→C `RECORDDATA` chunks | Character record with the character info record, field and position; `ItemCount` and `DataSize` are zero because inventory stays on the server. Chunking and `Total` follow the original record chunks. |
| Entry failure | S→C `FAILCONNECT` with `EParam` = 3 | `LParam` = `EnterWorldFailure`. |
| Join | C→S `PLAYERINFO` (504 B), S→C `CONNECTED` | `CONNECTED` = {`LParam` server milliseconds, `WParam` object serial}. |
| Heartbeat | `CHECK_NETSTATE` every 4 s | Echoed with `SParam` = server time. The server closes connections silent for 60 s. |
| Client save | C→S `RECORDDATA` | Acknowledged with `RECORDRESULT` and discarded; the server owns the record. |

Successful creation and deletion send a fresh `ID_SETUSERINFO` before the echo.

## In-world messages

| Message | Direction | Use |
| --- | --- | --- |
| `TRANSPLAYDATA` | C→S | The client's own position, facing, motion (`angle[3]`), action target (`dwTargetSerial`) and field (`wArea[0]`). Sent at 5 Hz while moving, on state changes and every second while idle. |
| `PLAYDATAGROUP` | S→C | Nearby objects: a play data packet with one play buffer (84 B) or the coarse `PLAYDATA1` entry (40 B). |
| `GETPLAYERINFO` / `PLAYERINFO` | C→S / S→C | Describes an unknown object or one whose `bUpdateInfo[0]` revision changed. Monsters and NPCs are cached by `dwAutoCharCode`, as in the original client. |
| `WARPFIELD` | S→C | `{WParam field, SParam x, LParam z}` confirms a field change; `EParam` = 1 marks a correction after a rejected report. |
| `ATTACK_DAMAGE` | C→S | Attack data packet with the original damage checksum and damage cipher. Monster: attack lock (`AttackState` bit 0 held, `AttackCount` = sequence). NPC: opens it. |
| `WHISPERMESSAGE` | S→C | NPC dialogue lines, sent before the NPC result. |
| `PLAYITEM` / `DELITEM` | S→C | Ground items within the monster view range (`0xC8000` squared units); `lpStgArea` carries the drop handle and `type` the quantity. |

`Power` in `ATTACK_DAMAGE` is ignored. Field values that differ from the original:

- `angle[3]` holds motion state codes instead of a model motion index; the server has no per-model motion table.
  The original server already sends state codes in far `PLAYDATA1` entries.
- `StartPosi` of the play data packet carries the low 32 bits of the attack request sequence.
- `dwTemp[11]` of the character info record holds the template code, model scale, NPC services, face and hair, equipped
  weapon, armour and off-hand item codes, the village flag and the attack animation rate (`CharacterInfo.Temp*`
  constants).
  The character slot record uses `dwArmorCode` and `dwTemp[13]` for the same appearance data.
- Item identities on the wire are the original category | ordinal codes (`LegacyItemCode`).

### Authority

The server validates client reports instead of trusting them. A position report is rejected when it moves farther than
1.35 times the run speed over the elapsed time plus 32 units, when the player is dead, or when it drifts more than 192
units during a swing, flinch or potion. With the field's stage loaded, a report is also rejected when no walkable floor
lies under it within 64 units of climb, or when it covers more than 48 units through a wall; the accepted height is the
floor height. The server then answers with `WARPFIELD` and `EParam` = 1. A field change in
`wArea[0]` goes through the same gate and warp checks as before. The client walks into attack range itself, and the server
swings only once the reported position is in range. Damage, experience, drops and inventory are computed on the server.

## Etervale extension opcodes

The high word `0x4554` ("ET") is unused by the original protocol, so an original client ignores these messages. They keep the
`{size, code}` header and the 8192-byte frame limit.

| Code | Name | Direction | Content |
| --- | --- | --- | --- |
| `0x45540100` | `OwnerStatus` | S→C | Level, 64-bit experience, money, resources, attributes, combat values and revisions. |
| `0x45540101` | `Skills` | S→C | Skill revision and (code, rank) pairs. |
| `0x45540102` | `Inventory` | S→C | Inventory revision split into parts of up to 100 items. |
| `0x45540110` / `0x45540111` | `InventoryCommand` / `InventoryResult` | C→S / S→C | Transactional inventory operation, correlated by operation id. Ground pickups address drops by handle. |
| `0x45540120` / `0x45540121` | `ProgressionCommand` / `ProgressionResult` | C→S / S→C | Attribute allocation and skill learning. |
| `0x45540130` | `NpcResult` | S→C | Completes an NPC click with services and a message. |
| `0x45540140` | `CombatEvents` | S→C | Resolved swings, hits, potions and monster skill impacts. The original lets each client roll its own hits. |
| `0x45540150` | `RemoveObjects` | S→C | Serials leaving the receiver's view. The original client times objects out after 4–30 seconds. |
| `0x45540160` | `LeaveWorld` | C→S, echo | Leave the world after the server saves the character. |
| `0x45540170` | `PresenceClock` | S→C | Server tick of the presence entries that follow (see below). |

`PresenceClock` is 20 bytes: `{int32 size, uint32 code, uint16 Version = 1, uint16 Flags = 0, uint32 ServerTick,
uint32 TickIntervalMs = 50}`. The server sends it once per tick, before the first `PLAYDATAGROUP` or `CombatEvents` of that
tick, so every sample and event until the next clock belongs to `ServerTick × TickIntervalMs` on the server timeline.
Readers accept newer versions that append fields. The original `PLAYDATAGROUP` layout and its `dwSendCode` counter are
unchanged; a client that ignores the extension places samples by arrival time.

The client plays remote objects back on that timeline: it keeps the timestamped samples of each object and interpolates
position, the server's height and facing (shortest arc) linearly at a render time 150 ms behind the server, longer when the
object's samples arrive at the 2 Hz far-ring cadence. Motion states and combat events of the object apply when the render
time reaches them. A walking or running object may be extrapolated for 250 ms when samples are late, and a jump above 256
units snaps. Walk and run clips play at a rate proportional to the interpolated ground speed, and a walk or run state
without ground motion is shown as standing.

## Server capacity

The original server ran all game logic on one thread under one lock, scanned every connection and monster slot for
visibility, and held 600 suspended worker threads with a shared 1024-slot send queue. Its
default cap was 800 players per process. Etervale keeps the original view ranges and distance-weighted detail and changes
the mechanics:

- Fields step in parallel on the tick. They share only immutable catalogs and the thread-safe id allocator.
- Each field snapshot carries an `EntityGrid` built once per tick, so a receiver queries nearby cells instead of
  scanning the field.
- `PresenceEncoder` sends an object only when it changed: spawns and discrete changes (motion, HP, target, attack,
  description) immediately, positions at 5 Hz inside the high-detail range and 2 Hz out to the connect range. Position
  updates share a budget of 2048 bytes per receiver per tick, ranked by how overdue they are, which plays the role of the
  original per-receiver user limit and round-robin cursor.
- Sockets use .NET asynchronous I/O (IOCP on Windows) with pooled buffers and coalesced writes.
- Every field's stage collision (`Shared/PristonTale.Geometry`) is loaded at startup, as the original server does:
  42 stages, about 1.9 million collision triangles, 178 MiB, about 0.2 s. Monsters step like the original server's
  next-move check (slide along walls with two side probes, climb 20 units, collide only with other monsters);
  NPCs stop at walls and steps above 10 units; spawns, respawns and drops settle on the floor. The client builds its
  collision from the same `CollisionMesh`.
- SQLite runs in WAL mode with `synchronous=NORMAL`. The rollback journal limited a login burst to a few hundred writes
  per second.

Measured on one Windows machine running the server and all bots together, every bot in the same spot of Ricarten (the
densest case), walking and reporting at 5 Hz:

| Bots | Failures | World tick p50 / p99 | Data per bot |
| --- | --- | --- | --- |
| 200 | 0 | 0.80 ms / 1.21 ms | 81 KiB/s before the movement budget |
| 500 | 0 | 1.18 ms / 3.95 ms | 42 KiB/s |
| 1000 | 0 | 2.27 ms / 7.61 ms | 40 KiB/s |

The tick budget is 50 ms. These numbers cover one process and one field; they do not include monsters fighting players
or a separate client machine. Reproduce them with:

```bash
dotnet run --project tools/PristonTale.DevAccounts -c Release --no-build -- --sqlite <copy.db> create-bots bot 1000 bot-pw
dotnet run --project Server/PristonTale.Server -c Release --no-build -- --Database:SqlitePath=<copy.db> --Network:MaxConnectionsPerIp=0
dotnet run --project tools/PristonTale.LoadTest -c Release --no-build -- --count 1000 --seconds 60 --ramp 100
```

The server logs tick percentiles and bandwidth every 30 seconds.
