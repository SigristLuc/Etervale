# Network protocol

Etervale is online only. The client and server speak the original Priston Tale TCP protocol of the English C++ client in
`original_references/src`: the same framing, cipher, opcodes and 32-bit x86 struct layouts. The server stays
authoritative, so a few messages keep the original layout but change meaning, and operations the original client
performed locally use Etervale extension opcodes. The codecs live in this project; the server edge is
[`Server/PristonTale.Server/Net`](../../Server/PristonTale.Server/Net) and the client edge is
[`Client/PristonTale.Client.Net`](../../Client/PristonTale.Client.Net).

## Transport

| Item | Value | Source |
| --- | --- | --- |
| Port | TCP 10009 (`TCP_SERVPORT`, English build) | `network/transport/smwsock.h` |
| Frame | `int32 size` (including the header), `uint32 code`, at most 8192 bytes | `smwsock.cpp` |
| Cipher | `EncodePacket`: XOR `0xED`, running key, 16-bit byte-sum checksum, code `0x8001xxxx` | `smwsock.cpp`, `Language/language.h` |
| Encrypted limit | packets of 8176 bytes or more go out in plaintext, as `Send2` does | `smwsock.cpp` |
| Replay check | counter starts at `0x0010`; repeats of the last 16 and counters 16 behind the highest are dropped | `CheckEncRecvPacket` |

`PristonConnection` reads into a fixed buffer, decodes encrypted frames, and hands each packet to a handler before reading
the next one. Sends from any thread append to one pending buffer that a single writer flushes, so all packets queued
during one socket write leave together. A peer whose pending bytes exceed the limit (512 KiB) is disconnected; the
original dropped packets once its global send queue was full.

Deviations from the original transport:

- `ENCODE_PACKET2` (`0x9001xxxx`) is rejected. The original only used it with `*CRAZY_PACKET`, where the server pushes
  x86 machine code to the client.
- The replay window compares counters modulo 65536. The original compares plain integers and stops accepting
  encrypted packets after about 65,520 of them in one direction.

## Login, character select and world entry

| Step | Packet | Notes |
| --- | --- | --- |
| Connect | S→C `VERSION` | `WParam` = minimum client version (167); the client sends `CLIENT_VERSION_NUM` 3122 later. |
| Log in | C→S `ID_GETUSERINFO` (`smTRANS_CHAR_COMMAND2`, 84 B) | `szId` = account, `szName` = password, as in the original. |
| Success | S→C `ID_SETUSERINFO` (1484 B) and `SERVER_INFO` | Character list and the one game server at this address. |
| Failure | S→C `FAILCONNECT` | `WParam` uses the original codes: -2 bad credentials, -4 already logged in, -16 storage busy, -24 denied. |
| Name check | `ISRECORDDATA` echo | `WParam` = 1 when the name exists. |
| Create | `INSRECORDDATA` echo | Request `LParam` = `JOB_CODE`, `SParam` = face (unset in the original). Echo `WParam` = 1/0, `LParam` = reason. |
| Delete | `DELRECORDDATA` echo | The original sends no reply; Etervale echoes the result. |
| Select | C→S `GETRECORDDATA` (52 B) | `SParam` = client version. |
| Record | S→C `RECORDDATA` chunks | `TRANS_RECORD_DATA` with `smCHAR_INFO`, field and position; `ItemCount` and `DataSize` are zero because inventory stays on the server. Chunking and `Total` follow `TRANS_RECORD_DATAS`. |
| Entry failure | S→C `FAILCONNECT` with `EParam` = 3 | `LParam` = `EnterWorldFailure`. |
| Join | C→S `PLAYERINFO` (504 B), S→C `CONNECTED` | `CONNECTED` = {`LParam` server milliseconds, `WParam` object serial}. |
| Heartbeat | `CHECK_NETSTATE` every 4 s | Echoed with `SParam` = server time. The server closes connections silent for 60 s. |
| Client save | C→S `RECORDDATA` | Acknowledged with `RECORDRESULT` and discarded; the server owns the record. |

Successful creation and deletion send a fresh `ID_SETUSERINFO` before the echo.

## In-world messages

| Message | Direction | Use |
| --- | --- | --- |
| `TRANSPLAYDATA` | C→S | The client's own position, facing, motion (`angle[3]`), action target (`dwTargetSerial`) and field (`wArea[0]`). Sent at 5 Hz while moving, on state changes and every second while idle. |
| `PLAYDATAGROUP` | S→C | Nearby objects: `smTRANS_PLAYDATA` with one play buffer (84 B) or the coarse `smPLAYDATA` (40 B). |
| `GETPLAYERINFO` / `PLAYERINFO` | C→S / S→C | Describes an unknown object or one whose `bUpdateInfo[0]` revision changed. Monsters and NPCs are cached by `dwAutoCharCode`, as in `playsub.cpp`. |
| `WARPFIELD` | S→C | `{WParam field, SParam x, LParam z}` confirms a field change; `EParam` = 1 marks a correction after a rejected report. |
| `ATTACK_DAMAGE` | C→S | `TRANS_ATTACKDATA2` with `dm_GetDamgeChkSum` and `dm_EncodePacket`. Monster: attack lock (`AttackState` bit 0 held, `AttackCount` = sequence). NPC: opens it. |
| `WHISPERMESSAGE` | S→C | NPC dialogue lines, sent before the NPC result. |
| `PLAYITEM` / `DELITEM` | S→C | Ground items within `MHIGH`; `lpStgArea` carries the drop handle and `type` the quantity. |

`Power` in `ATTACK_DAMAGE` is ignored. Field values that differ from the original:

- `angle[3]` holds `CHRMOTION_STATE_*` codes instead of a model motion index; the server has no per-model motion table.
  The original server already sends state codes in far `smPLAYDATA` entries.
- `smTRANS_PLAYDATA.StartPosi` carries the low 32 bits of the attack request sequence.
- `smCHAR_INFO.dwTemp[11]` holds the template code, model scale, NPC services, face and hair, equipped weapon, armour
  and off-hand item codes, the village flag and the attack animation rate (`CharacterInfo.Temp*` constants).
  `_TRANS_CHAR_INFO` uses `dwArmorCode` and `dwTemp[13]` for the same appearance data.
- Item identities on the wire are the original `sinXX1|sinNN` codes (`LegacyItemCode`).

### Authority

The server validates client reports instead of trusting them. A position report is rejected when it moves farther than
1.35 times the run speed over the elapsed time plus 32 units, when the player is dead, or when it drifts more than 192
units during a swing, flinch or potion. With the field's stage loaded, a report is also rejected when no walkable floor
lies under it within 64 units of climb, or when it covers more than 48 units through a wall; the accepted height is the
floor height. The server then answers with `WARPFIELD` and `EParam` = 1. A field change in
`wArea[0]` goes through the same gate and warp checks as before. The client walks into attack range itself, and the server
swings only once the reported position is in range. Damage, experience, drops and inventory are computed on the server.

## Etervale extension opcodes

The high word `0x4554` ("ET") is unused by `smPacket.h`, so an original client ignores these messages. They keep the
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

## Server capacity

The original server ran all game logic on one thread under one lock, scanned every connection and monster slot for
visibility, and held 600 suspended worker threads with a shared 1024-slot send queue (`smwsock.cpp`, `OnSever.cpp`). Its
default cap was 800 players per process. Etervale keeps the original view ranges and distance-weighted detail and changes
the mechanics:

- Fields step in parallel on the tick. They share only immutable catalogs and the thread-safe id allocator.
- Each field snapshot carries an `EntityGrid` built once per tick, so a receiver queries nearby cells instead of
  scanning the field.
- `PresenceEncoder` sends an object only when it changed: spawns and discrete changes (motion, HP, target, attack,
  description) immediately, positions at 5 Hz inside `HIGH` and 2 Hz out to `CONNECT`. Position updates share a budget of
  2048 bytes per receiver per tick, ranked by how overdue they are, which plays the role of `TRANS_MAX_USER_LIMIT` and the
  original round-robin cursor.
- Sockets use .NET asynchronous I/O (IOCP on Windows) with pooled buffers and coalesced writes.
- Every field's stage collision (`Shared/PristonTale.Geometry`) is loaded at startup, as `srReadStage` does in the
  original: 42 stages, about 1.9 million collision triangles, 178 MiB, about 0.2 s. Monsters and NPCs follow the floor
  and stop at walls and steps above 10 units; spawns, respawns and drops settle on the floor. The client builds its
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
dotnet run --project Server/PristonTale.Server -c Release --no-build -- --Database:SqlitePath=<copy.db>
dotnet run --project tools/PristonTale.LoadTest -c Release --no-build -- --count 1000 --seconds 60 --ramp 100
```

The server logs tick percentiles and bandwidth every 30 seconds.
