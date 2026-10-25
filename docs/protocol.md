# Network protocol

Etervale is online only. Client and server speak the original Priston Tale TCP protocol of the English client: the same
framing, cipher, opcodes and 32-bit x86 struct layouts. The server stays authoritative, so a few messages keep the
original layout but change meaning, and operations the original client performed locally use Etervale extension opcodes.
The codecs live in `Shared/PristonTale.Protocol`; the server edge is `Server/PristonTale.Server/Net`
([server.md](server.md)) and the client edge is `Client/PristonTale.Client.Net` ([client.md](client.md)).

## Read this when

- You add an opcode, a packet or a field, or change a layout or size.
- A packet fails to decode, the connection closes with `protocol: ...`, or sizes disagree between client and server.
- You need the meaning of a `TransCommand` parameter (`LParam`/`WParam`/`SParam`/`EParam`) for a given opcode.
- You work on presence: play data, the presence clock, remote playback timing, view ranges or bandwidth.
- You change server movement authority, skill cast validation or NPC service messages on the wire.
- You measure or tune server capacity.

## Code map

| Path | Responsibility |
| --- | --- |
| [Binary/PristonOpcodes.cs](../Shared/PristonTale.Protocol/Binary/PristonOpcodes.cs) | `PristonOpcodes` (original codes) and `EtervaleOpcodes` (`0x4554xxxx` extensions). |
| `Binary/PristonFrameCodec.cs` | `PristonFrameCodec` (header, 8192-byte limit) and `PristonPacketCipher` (encrypted envelope). |
| `Binary/PristonReplayWindow.cs` | Replay check of encrypted receive counters. |
| `Binary/PacketReader.cs`, `Binary/PacketWriter.cs` | Little-endian sequential reader/writer; the writer patches the size in `Finish()`. |
| `Binary/PristonText.cs` | Fixed `char[]` fields: Windows-1252, NUL-terminated (`Strict` throws, `Lenient` replaces with `?`). |
| `Binary/PristonServerListCodec.cs` | `SERVER_INFO` (44-byte header, 108-byte entries, at most 8). |
| `Packets/TransCommand.cs` | 24-byte command, 40-byte extended command, `CharCommand` (52 B) and `CharCommand2` (84 B). |
| `Packets/PlayData.cs` | `PristonUnits`, `MotionStateCode`, play buffer, `TransPlayData` (the client's movement report). |
| `Packets/PresencePackets.cs` | `PresenceEntries`: `VarInt`, `PresenceMotion`, `PresenceEntry`, `PresenceWire` (entry codec), writer and reader. |
| `Packets/CharacterInfo.cs` | 464-byte character info record, `dwTemp` slot constants, 504-byte `PLAYERINFO`. |
| `Packets/RecordData.cs` | Character record (1776-byte fixed part), `RecordDataChunks.Split` and `RecordDataAssembler`. |
| `Packets/UserCharacterList.cs` | `ID_SETUSERINFO` character list (1484 B, six 240-byte slots). |
| `Packets/AttackDamagePacket.cs` | 92-byte `ATTACK_DAMAGE` with the damage checksum and damage cipher. |
| `Packets/GroundItems.cs` | `PLAYITEM` list, `DELITEM`/`PUTITEM` action, chat message packet. |
| `Packets/ChatInputPacket.cs`, `ChatChannelCode.cs` | The typed chat line the client sends; chat kinds on the wire. |
| `Packets/EtervalePackets.cs` | Owner status, inventory parts, skills, inventory/progression commands and results, combat events, removals, presence clock. |
| `Packets/ItemPackets.cs` | Owner item details (`ItemDetailEntry`, `ItemDetailsPartPacket`) and the equipment status (`OwnerItemStatusPacket`). |
| `Packets/SkillPackets.cs` | Skill cast, cast result, skill events, status effects, cooldowns. |
| `Packets/NpcPackets.cs` | NPC result, service request/result, shop catalog and quotes, warehouse, teleport list. |
| [Transport/PristonConnection.cs](../Shared/PristonTale.Protocol/Transport/PristonConnection.cs) | One TCP stream: receive loop, decryption, replay check, coalesced single-writer send. |
| `Transport/IPacketSink.cs` | `Send` (plaintext) / `SendEncrypted`; implemented by `PristonConnection` and test sinks. |
| `Server/PristonTale.Server/Net/` | `ClientSession` dispatch, `PresenceEncoder`, `WireMapping`, `NpcWireMapping`. |
| `Client/PristonTale.Client.Net/GameServerConnection.cs` | Client requests, heartbeat, request/reply correlation. |
| `Client/PristonTale.Client.Net/Online/PresenceDecoder.cs` | Client decoding of presence packets into `PresenceUpdate`s. |
| `tests/PristonTale.Tests/BinaryProtocolTests.cs`, `SkillWireTests.cs`, `NpcServiceTests.cs`, `WireRoundTrip.cs` | Layout, cipher, replay, connection and encoder-decoder round trips. |

The protocol project's `obj/` folder can contain stale generated sources (for example `messages/Session.cs`) that a text
search finds; they are not part of the build.

## Conventions

| Item | Rule |
| --- | --- |
| Byte order | Little-endian everywhere; structs follow 32-bit x86 packing (pointers are 4-byte zero slots). |
| Header | `{int32 size, uint32 code}`; `size` includes the header. |
| Positions | Fixed point, world units × 256 (`PristonUnits.ToFixed`, `FixedShift` 8). |
| Angles | 4096 per turn; yaw in `angle[1]` (`PristonUnits.ToAngle`). |
| Fixed text | `char[N]` Windows-1252, NUL-terminated, at most N-1 bytes (`PristonText`). |
| Extension text | NPC messages: UTF-8 with a `uint16` byte length, truncated to the field's byte limit. |
| Item identity | Original category ordinal codes (`LegacyItemCode`), e.g. gold is `GG101`. |
| Reading | `PacketReader` requires `size == span length` and `size >= minimum`; trailing bytes are accepted. Overruns throw `InvalidDataException`. |
| Versioned bodies | Extension messages with `{uint16 Version, uint16 Flags}` reject version 0. Fixed-size bodies accept newer versions that append fields; list bodies check their length exactly. |

## Transport

| Item | Value |
| --- | --- |
| Port | TCP 10009 (English build); the server's `Network:Port` can change it |
| Frame | `int32 size` (including the header), `uint32 code`, 8 to 8192 bytes; anything else closes the connection |
| Cipher | Encrypted envelope: code `0x8001xxxx` (low word = 16-bit byte-sum checksum), XOR `0xED`, running key, 8 bytes of overhead |
| Key seed | High byte = first octet of the peer's IPv4 address, low byte random; counter in the envelope |
| Encrypted limit | Packets of 8176 bytes or more go out in plaintext, as in the original |
| Replay check | Send counter starts at `0x0010`; a received counter that repeats one of the last 16 or is more than 16 behind the highest is dropped silently |
| Send backlog | More than 512 KiB pending closes the connection |

`PristonConnection` reads into a 16 KiB buffer, decodes encrypted frames, and awaits the handler for each packet before
reading the next one. The packet memory is valid only until the handler's task completes. Sends from any thread append
to one pending buffer that a single writer flushes, so all packets queued during one socket write leave together. The
original dropped packets once its global send queue was full. A failed checksum closes the connection.

Deviations from the original transport:

- The second envelope (`0x9001xxxx`) is rejected and closes the connection. The original only used it with a decoder
  that the server pushes to the client as x86 machine code.
- The replay window compares counters modulo 65536. The original compares plain integers and stops accepting encrypted
  packets after about 65,520 of them in one direction.

Which side encrypts what is a sender choice; receivers accept either form for every code. The server encrypts command
replies, login, record and NPC/inventory/progression/skill-cast results and `WARPFIELD`; presence traffic (play data,
events, drops, owner state, removals, clock, `PLAYERINFO` descriptions) goes out in plaintext. The client encrypts
login, character commands, heartbeats, `ATTACK_DAMAGE`, `SkillCast` and operation requests, and sends `GETRECORDDATA`,
`PLAYERINFO`, `TRANSPLAYDATA` and `GETPLAYERINFO` in plaintext.

## Login, character select and world entry

`TransCommand` parameters are listed by name; on the wire the order is `LParam, WParam, SParam, EParam`.

| Step | Packet | Notes |
| --- | --- | --- |
| Connect | S→C `VERSION` | `WParam` = minimum client version (167); `LParam` = players in the world when a game master's `/user_limit` is reached, else 0 (then only game masters may log in). The client reports its own version (3122) in `GETRECORDDATA`. |
| Log in | C→S `ID_GETUSERINFO` (account command packet, 84 B) | `szId` = account, `szName` = password, as in the original. |
| Success | S→C `ID_SETUSERINFO` (1484 B) and `SERVER_INFO` (152 B) | Character list and the one game server: name `Etervale`, the login socket's local IPv4 address and port. |
| Failure | S→C `FAILCONNECT`, `EParam` = 2 | `WParam` uses the original codes: -2 bad credentials, -4 already logged in on this connection, -16 busy (throttled or storage failure), -24 account blocked. |
| Name check | `ISRECORDDATA` echo | `WParam` = 1 when the name exists. |
| Create | `INSRECORDDATA` echo | Request `LParam` = class code, `SParam` = face (unset in the original). Echo `WParam` = 1/0, `LParam` = `CreateCharacterFailure`. |
| Delete | `DELRECORDDATA` echo | The original sends no reply; Etervale echoes the result in `WParam`. |
| Select | C→S `GETRECORDDATA` (52 B) | `SParam` = client version, `szName` = character. |
| Record | S→C `RECORDDATA` chunks | Character record with the character info record, field and position; `ItemCount` and `DataSize` are zero because inventory stays on the server. Chunking and `Total` follow the original record chunks (8160 data bytes per chunk; a record that fits one chunk declares `Total` = 0). |
| Entry failure | S→C `FAILCONNECT` with `EParam` = 3 | `LParam` = `EnterWorldFailure`. |
| Join | C→S `PLAYERINFO` (504 B), S→C `CONNECTED` | `PLAYERINFO.ObjectSerial` must equal the serial from the record. `CONNECTED` = {`LParam` server milliseconds, `WParam` object serial}. |
| Heartbeat | `CHECK_NETSTATE` every 4 s | Client `LParam` = counter, `WParam` = client time; echoed with `SParam` = server time. The server closes connections silent for 60 s. |
| Client save | C→S `RECORDDATA` | Acknowledged with `RECORDRESULT` (`WParam` = 1) after the last chunk and discarded; the server owns the record. |
| Leave | `LeaveWorld` (extension) | Echoed with `WParam` = 1 after the server saved the character. |

Successful creation and deletion send a fresh `ID_SETUSERINFO` before the echo. Storage failures answer the pending
request (`FAILCONNECT` -16, a record echo with `WParam` 0, or entry failure `Rejected`) instead of closing.

## In-world messages

| Message | Direction | Use |
| --- | --- | --- |
| `TRANSPLAYDATA` | C→S | The client's own position, facing, motion (`angle[3]`), action target (`dwTargetSerial`) and field (`wArea[0]`). Sent every 200 ms while moving, on state changes (at most one per 50 ms) and every second while idle. |
| `PresenceEntries` (`0x45540900`) | S→C | Nearby objects as compact full or motion entries (see [Presence entries](#presence-entries)). |
| `GETPLAYERINFO` / `PLAYERINFO` | C→S / S→C | Describes an unknown object or one whose `bUpdateInfo[0]` revision changed (`GETPLAYERINFO.WParam` = serial). Monsters and NPCs are cached by `dwAutoCharCode`, as in the original client; owned summons and crystal pets carry `dwAutoCharCode` 0 and are always described on their own. The client asks again after a second without an answer (the server drops requests beyond its bucket). |
| `WARPFIELD` | S→C | `{WParam field, SParam x, LParam z}` confirms a field change; `EParam` = 1 marks a correction after a rejected report or a teleport. |
| `ATTACK_DAMAGE` | C→S | Attack data packet with the original damage checksum and damage cipher keyed by the sender's serial. Monster: attack lock (`AttackState` bit 0 held, `AttackCount` = sequence). NPC: opens it. |
| `PLAYITEM` / `DELITEM` | S→C | Ground items within the ground-item range (squared `0xC8000` units, about 905 units); up to 16 records per `PLAYITEM`. `lpStgArea` carries the drop handle and `type` the quantity. Private drops go only to their owner. |
| `CHATMESSAGE` / `PARTY_CHATMSG` | C→S | A typed line `{size, code, text}` (size 9 + length; `ChatInputPacket`): spoken line or command, or a party line. |
| `CHATMESSAGE` / `WHISPERMESSAGE` | S→C | `{size, code, kind, speaker serial, text}` (size 32 + length): a line over a character, or a line for the log only. Kinds and routing: [chat-and-gm.md](chat-and-gm.md#wire). |
| `CLOSECLIENT` | S→C | Asks the client to quit (the end of a game master's shutdown countdown). |

`Power` in `ATTACK_DAMAGE` is ignored. Field values that differ from the original:

- `angle[3]` of the client's movement report holds motion state codes (`MotionStateCode`) instead of a model motion
  index; the server has no per-model motion table.
- `dwTemp[11]` of the character info record holds the template code, model scale, NPC services (bit `1 <<
  NpcServiceKind`), face and hair, equipped weapon, armour and off-hand item codes, the village flag and the attack
  animation rate (`CharacterInfo.Temp*` constants). A summon is described with the user brood, its owner's serial in
  `dwTemp[9]` and its own model scale (float bits, 0 keeps the template's) in `dwTemp[10]`. The character slot record
  uses `dwArmorCode` and `dwTemp[13]` for the same appearance data.
- A full presence entry carries the descriptor revision (`bUpdateInfo[0]` of the description, 1 to 255). The field
  keeps one revision per entity for every viewer (`FieldDescriptors`) and increments it when level, max HP, template,
  speed, attack rate, equipment, looks (hair, face, pets, big head), village flag, tier, owner or scale change.

### Authority

The server validates client reports instead of trusting them (`WorldPlayer.ReportMovement`):

- Reports spend a distance budget that refills at 1.35 times the run speed and holds at most two seconds of travel plus
  32 units, so many small reports cannot add up to more than one long report could.
- During a swing, flinch or potion, drift beyond 192 units is rejected and smaller drift is ignored. Reports from a dead
  player are ignored without a correction.
- With the field's stage loaded, a report up to 48 units long follows the wall slide and may end at most 8 units from
  the reported point; a longer report may not cross a wall. A report is rejected when no walkable floor lies under it
  within 64 units of climb; the accepted height is the floor height.

A rejected report is answered with `WARPFIELD` and `EParam` = 1 (at most every 300 ms). A field change in `wArea[0]` is
accepted only when the player stands at a warp gate or field gate leading to that field and meets its level limit
(`FieldTransition.TryResolve`); otherwise the server forces the player back. The client walks into attack range itself,
and the server swings only once the reported position is in range. Damage, experience, drops and inventory are computed
on the server.

## Etervale extension opcodes

The high word `0x4554` ("ET") is unused by the original protocol, so an original client ignores these messages. They
keep the `{size, code}` header and the 8192-byte frame limit.

| Code | Name | Direction | Content |
| --- | --- | --- | --- |
| `0x45540100` | `OwnerStatus` | S→C | Level, unsigned 64-bit experience (current, level start, next level), 64-bit money, resources, attributes, combat values and revisions, then the final-tier skill points and the class tier (0..3). 156 B. |
| `0x45540101` | `Skills` | S→C | Skill revision and (code, rank) pairs, at most 512. |
| `0x45540102` | `Inventory` | S→C | Inventory revision split into parts of up to 100 items; the client replaces its inventory when all parts of a revision arrived. |
| `0x45540110` / `0x45540111` | `InventoryCommand` / `InventoryResult` | C→S / S→C | Transactional inventory operation, correlated by operation id. A ground pickup (`InventoryAction.Pickup`) names the drop in `ItemId`. |
| `0x45540120` / `0x45540121` | `ProgressionCommand` / `ProgressionResult` | C→S / S→C | Attribute allocation and skill learning. |
| `0x45540130` | `NpcResult` | S→C | Completes an NPC click: name, typed services with availability and the line the NPC says. |
| `0x45540131` / `0x45540132` | `NpcServiceRequest` / `NpcServiceResult` | C→S / S→C | One typed NPC service request (open, close, refresh, buy, sell, repair, warehouse moves and gold, teleport), correlated by operation id. |
| `0x45540133` | `ShopCatalog` | S→C | The NPC's stock with server prices (at most 96), sent before the result of a shop open. |
| `0x45540134` | `ShopQuotes` | S→C | Sell price and repair cost of every owned item at the inventory revision (at most 128). |
| `0x45540135` | `Warehouse` | S→C | The account warehouse: revision, gold, weight and items by grid cell (at most 100). |
| `0x45540136` | `TeleportList` | S→C | A teleporter's destinations with price and minimum level (at most 16). |
| `0x45540140` | `CombatEvents` | S→C | Resolved swings, hits, potions and monster skill impacts. The original lets each client roll its own hits. |
| `0x45540150` | `RemoveObjects` | S→C | Serials leaving the receiver's view. The original client times objects out after 4-30 seconds. |
| `0x45540160` | `LeaveWorld` | C→S, echo | Leave the world after the server saves the character. |
| `0x45540170` | `PresenceClock` | S→C | Server tick of the presence entries that follow (see below). |
| `0x45540200` | `SkillCast` | C→S | Cast request (see below). |
| `0x45540201` | `SkillCastResult` | S→C | Answer to the caster's own cast, by sequence. |
| `0x45540202` | `SkillEvents` | S→C | Skill starts, impacts, releases and cancels on the presence timeline. |
| `0x45540203` | `StatusEffects` | S→C | Modifier effects added, refreshed, removed or ticking, on the presence timeline. |
| `0x45540204` | `SkillCooldowns` | S→C | The owner's running cooldowns whenever the set changes. |
| `0x45540300` | `ItemDetails` | S→C | The values of every owned item copy at an inventory revision, in parts sent before its `Inventory` parts. |
| `0x45540301` | `OwnerItemStatus` | S→C | Carried and maximum weight, critical, block, range, attack speed, belt capacity, regeneration, magic mastery and the eight resistances; sent before every `OwnerStatus`. |
| `0x45540400` / `0x45540401` | `ItemUseCommand` / `ItemUseResult` | C→S / S→C | Use of one carried item, correlated by operation id; a travel item also sends a forced `WARPFIELD`. |
| `0x45540402` | `PremiumTimers` | S→C | The owner's running premium timers whenever they change. |
| `0x45540403` | `ItemEffects` | S→C | Item effects around players (firecrackers, learn ring, mastery, pet strikes), on the presence timeline. |
| `0x45540500` / `0x45540501` | `QuestRequest` / `QuestResult` | C→S / S→C | One quest step (talk, proceed, accept, class change, reward choice, skill redistribution), correlated by operation id (see below). |
| `0x45540502` | `QuestJournal` | S→C | The owner's quest journal view whenever its revision changes. |
| `0x45540503` | `QuestNotices` | S→C | Quest notices of the world tick and a quest dialog the world opened. |
| `0x45540A00` / `0x45540A01` | `RestartRequest` / `RestartResult` | C→S / S→C | A dead player's restart (field, town, exit), correlated by operation id; a stored restart also sends a forced `WARPFIELD`. |
| `0x45540A02` | `RestartOffer` | S→C | While the owner is dead, what a restart would cost, whenever it changes. |

### Owner, inventory and combat layouts

These bodies have no version header.

| Message | Layout after the header | Size |
| --- | --- | --- |
| `InventoryCommand` | `Guid OperationId, int64 ExpectedRevision, int32 Action, Guid ItemId, int32 Destination, Count, Slot` | 64 |
| `ProgressionCommand` | `Guid OperationId, int64 ExpectedRevision, int32 Action, Attribute, Amount, uint SkillCode, NpcSerial` | 52 |
| `InventoryResult` / `ProgressionResult` | `Guid OperationId, int32 Ok, int64 Revision, char Message[64]` | 100 |
| `Inventory` | `int64 Revision, int32 Part, Parts, Count`, then items `{Guid, uint Code, int32 Kind, Count, Anchor, Slot, int16 Width, Height, char Name[32]}` | 28 + 72n |
| `Skills` | `int64 Revision, int32 Count`, then `{uint Code, int32 Rank}` | 20 + 8n |
| `CombatEvents` | `int32 Count`, then entries of 48 bytes | 12 + 48n |
| `RemoveObjects` | `int32 Count`, then `uint Serial` | 12 + 4n |
| `ItemDetails` | `uint16 Version (1), uint16 Flags, int64 Revision, int32 Part, Parts, Count`, then 220-byte entries (at most 24) | 32 + 220n |
| `OwnerItemStatus` | `uint16 Version (1), uint16 Flags, int32 CarriedWeight, MaxWeight, CriticalChance, BlockChance, ShootingRange, AttackSpeed, PotionSpace, float LifeRegen, ManaRegen, StaminaRegen, MagicMastery, int16 Resistances[8]` | 72 |

An item details entry is `{Guid InstanceId, int32 DurabilityCurrent, DurabilityMax, UpgradeLevel, DamageMin, DamageMax,
AttackRating, Defence, AddLife, AddMana, AddStamina, RequiredLevel, RequiredStrength, RequiredSpirit, RequiredTalent,
RequiredAgility, RequiredHealth, Price, Critical, AttackSpeed, ShootingRange, PotionSpace, Weight, UniqueItem, Flags
(1 two-handed, 2 the owner's class may use it), uint SpecJob, int32 SpecDefence, SpecAttackSpeed, SpecCritical,
SpecShootingRange, SpecLevelLife, SpecLevelMana, SpecLevelAttackRating, SpecLevelDamage, float Absorb, Block,
MoveSpeed, LifeRegen, ManaRegen, StaminaRegen, MagicMastery, SpecAbsorb, SpecMoveSpeed, SpecBlock, SpecMagicMastery,
SpecLifeRegen, SpecManaRegen, SpecStaminaRegen, int16 Resistances[8]}` (`ItemDetailEntry`). The client attaches the
entries to the inventory of the same revision (`OwnedItemView.Details`) and folds the status into the owner state.

A combat entry is `{uint Attacker, Target, byte Kind, byte Flags (1 critical, 2 killed, 4 monster skill), int16 Clip,
float DurationMs, HitAtMs, int Damage, Resources, SoundCode, X, Y, Z, int16 Angle, int16 0}`. `Kind` is
`CombatEventKindView`: 1 swing begin, 2 hit, 3 potion begin, 4 potion applied, 5 monster skill impact, 6 heal (amount
in `Damage`), 7 evade, 8 revive (returned life in `Damage`), 9 block (a player blocked a monster's normal attack).
`Clip` is the motion-table slot of the clip a monster's swing plays (-1: the state's first clip); flag 4 marks a
monster's skill swing and the hits of its skill. A monster drinking a potion or changing shape sends the original
motions `Eat` and `Warp` in its presence entries.

### Skills and status effects

Every skill message body starts with `{uint16 Version, uint16 Flags}` (version 1, flags 0, unless stated); readers
reject version 0, and `SkillCast` and `SkillCastResult` accept newer versions that append fields (the list messages
check their length exactly). Positions are fixed point (×256, `PristonUnits`) and angles 4096 per turn.

| Message | Layout after the header | Size |
| --- | --- | --- |
| `SkillCast` (version 2) | `uint Sequence, uint SkillCode, uint TargetSerial, int X, Y, Z, int16 Angle, int16 0, uint ClientTimeMs` | 44 |
| `SkillCastResult` | `uint Sequence, uint SkillCode, byte Status, byte Rank, int16 0, uint CooldownMs, uint RemainingMs, int Mana, int Stamina, uint ServerTick` | 44 |
| `SkillEvents` (version 2) | `int Count`, then `Count` skill entries of 48 bytes | 16 + 48n |
| `StatusEffects` | `int Count`, then `Count` status entries of 32 bytes | 16 + 32n |
| `SkillCooldowns` | `uint ServerTick, int Count`, then `{uint SkillCode, uint RemainingMs, uint DurationMs}` (at most 256) | 20 + 12n |

`SkillCast` flags carry the command (`SkillCastFlags`): 0 a new cast, 1 the release of the held charge of that skill, 2
and 3 order the caster's summon of that skill to fight only the caster's target or to pick the nearest enemy; flags
above 3 fail to decode, and a version 1 cast is always a new cast. A zero `X, Y, Z` means "no ground point". The server
writes 0 in `SkillCastResult.ServerTick`.

A skill entry is `{uint Caster, Target, SkillCode, byte Phase (1 start, 2 impact, 3 cancel, 4 release), byte Rank,
uint16 Flags (1 area, 2 projectile, 4 self, 8 chain link from X, Y, Z to Target, 16 familiar strike, 32 support reaching
another player, 64 counter strike, 128 full charge, bits 8-9 the resolution mode drawn for the cast), float DurationMs,
HitAtMs, int X, Y, Z, int16 Angle, uint16 TargetCount, int Radius, int 0}`. A release entry gives the shortened
remaining motion in `DurationMs` and the time to its next hit in `HitAtMs`. A status entry is `{uint Entity, Source,
StatusCode, byte Op (1 add, 2 refresh, 3 remove, 4 tick), byte Stacks, byte SourceKind, byte Control, uint16 Flags (1
harmful, 2 permanent), int16 0, int DurationMs, RemainingMs, Value}`. Permanent entries carry zero timers.

The client sends only the skill, the target and its own position. The server takes rank, costs, range, facing and timing
from its own state and the class skill files (`Shared/PristonTale.Simulation/Skills/Data/Classes/<Class>.json`) and
refuses a cast that is unlearned, of another class, cooling down, short of mana or stamina, without the required weapon
or shield, out of range (rule range plus 24 units, `FieldSkills.RangeSlack`), aimed at a missing or dead target, cast in
a village, or sent while the player is dead, busy or stunned; `AlreadyActive` (14) refuses a buff whose effect, or one
it cannot combine with, is already running on its holder. `Status` is the refusal reason (`SkillCastStatus`, the wire
copy of `SkillCastVerdict`). An accepted cast spends mana and stamina, starts the cooldown, puts the caster in the skill
motion and emits a `Start` event whose `DurationMs` and `HitAtMs` are the motion's length and first event at the
server's rates; the hits resolve at the motion's event times (projectiles after their flight at 900 units per second,
`SkillProjectile.UnitsPerSecond`) and emit an `Impact` event with the number of targets hit, then the usual
`CombatEvents`. Statuses are source-agnostic: `SourceKind` is item, player skill, monster skill, consumable or aura,
`Control` the control states (stun, root, silence, slow, hidden); a viewer that starts seeing an entity (it came into
range, joined, or the entity arrived from another field) receives the statuses already running on it as `Add` entries
with the time left. The original client sent damage, buffs and heals itself and the original server checked
none of mana, stamina or cooldown. See [skills.md](skills.md) for the rules behind these messages.

### Usable items

Every body starts with `{uint16 Version = 1, uint16 Flags = 0}`; text is UTF-8 with a `uint16` byte length.

| Message | Layout after the header | Size |
| --- | --- | --- |
| `ItemUseCommand` | `Guid OperationId, int64 ExpectedRevision, Guid ItemId, int32 Argument, text Text (64 B)` | 58 + text |
| `ItemUseResult` | `Guid OperationId, int32 Ok, int64 Revision, uint16 Status, uint16 0, uint32 ItemCode, text Message (128 B)` | 50 + text |
| `PremiumTimers` | `int64 Revision, int32 Count`, then `{uint8 Kind, uint8 0, uint16 0, int32 Value, uint32 RemainingSeconds, DurationSeconds}` (at most 32) | 24 + 16n |
| `ItemEffects` | `int32 Count`, then `{uint32 Entity, ItemCode, uint8 Effect, uint8 0, uint16 0, int32 X, Y, Z, Variant}` (at most 64) | 16 + 28n |

`Status` is `ItemUseStatus`; `Kind` is `PremiumEffectKind`; `Effect` is `ItemEffectKind` (1 firecracker with its roll
in `Variant`, 2 mastery, 3 force orb, 4 learn, 5 pet strike with the pet kind in `ItemCode` and the target serial in
`Variant`, 6 return sparkle at the given position, 7 head change, 8 drink, 9 gift box). Player descriptors carry the
pets and the head look in `CharacterInfo.TempFlags` (bit 1 Phoenix, bits 8-11 helper pet, bits 12-13 head look: 1-2
big head, 3 soccer ball). A sealed puzzle piece travels as the PZ100 code with the name "Lucky Bag". The puzzle
collector's exchange is the NPC service action `Exchange` (40) on `QuestEvent`.
Rules: [special-items.md](special-items.md).

### Presence clock and remote playback

`PresenceClock` is 20 bytes: `{int32 size, uint32 code, uint16 Version = 1, uint16 Flags = 0, uint32 ServerTick, uint32
TickIntervalMs = 50}`. On a tick that produces timeline traffic for the receiver, the server sends it once, before the
first `PresenceEntries`, `CombatEvents`, `SkillEvents` or `StatusEffects` of that tick, so every sample and event until
the next clock belongs to `ServerTick × TickIntervalMs` on the server timeline. Readers reject version 0 and a zero
interval.

### Presence entries

`PresenceEntries` is `{int32 size, uint32 code, uint16 Version = 1, uint16 Flags = 0, uint32 Sequence, uint16 Count,
uint16 0}` followed by `Count` entries that fill the message exactly. An entry is `{varuint Serial, uint8 Kind (bit 0
full), int24 X, Y, Z (world units × 8), uint16 Facing (65536 per turn), uint8 Motion}`; a full entry adds `{varuint Hp,
MaxHp, Level, Target, AttackSequence, uint8 Revision, varuint Template, zigzag varint FieldCode}`. `varuint` is
little-endian base-128 (`VarInt`, up to 64 bits). `Motion` is `PresenceMotion` (0 stand, 1 walk, 2 run, 3 attack, 4
damage, 5 dead, 6 skill, 7 eat, 8 warp). Positions cover the world's ±1,000,000 units at 1/8 of a unit.

The server sends a full entry when an entity enters the receiver's view and whenever its motion, life, target, attack
sequence or description revision changes; otherwise a motion entry (about 14 bytes) at the near or far rate. Ids,
targets and the attack sequence are 64-bit on the wire; the world hands out 32-bit entity ids today and the client
rejects larger ones. `Template` is the shared template code (monsters and NPCs that are not owned), 0 otherwise;
`FieldCode` is -1 when not sent.

The client plays remote objects back on that timeline
(`Client/PristonTale.Gameplay/World/Remote/RemoteSnapshotBuffer.cs`): it keeps the timestamped samples of each object
and interpolates position, the server's height and facing (shortest arc) linearly at a render time 150 ms behind the
server, longer (up to 600 ms) when the object's samples arrive at the 2 Hz far-ring cadence. Motion states and combat
events of the object apply when the render time reaches them. A walking or running object may be extrapolated for 250 ms
when samples are late, and a jump above 256 units snaps. Walk and run clips play at a rate proportional to the
interpolated ground speed, and a walk or run state without ground motion is shown as standing.

### NPC services

Every NPC message body starts with `{uint16 Version = 1, uint16 Flags = 0}`; readers accept newer versions that append
fields, except `ShopQuotes`, whose list length is checked exactly. Text is UTF-8 with a `uint16` byte length (names up
to 64 bytes, item names 40, messages 128, dialog lines 256), and counts are 32-bit. The click itself stays the original
`ATTACK_DAMAGE` on the NPC serial; the server answers with `NpcResult` instead of the original whisper and
service-window commands.

- `NpcResult`: `{uint32 NpcSerial, int32 Ok, text Name, text Message, int32 ServiceCount, services {uint8 Kind, uint8
  Available, uint16 0, int32 Value}, int32 LineCount, text lines}` (at most 32 services and 20 lines). `Kind` is
  `NpcServiceKind`, `Value` the authored argument (class-change group, teleport route, mix mode, quest code...). One
  dialog line is sent, chosen at random as the original does.
- `NpcServiceRequest` (88 bytes): `{Guid OperationId, uint32 NpcSerial, uint8 Service, uint8 Action, uint16 0, int64
  ExpectedRevision, int64 ExpectedStorageRevision, uint32 ItemCode, Guid ItemId, int32 Anchor, int32 Count, int64
  Amount}`. The client names a stock entry, an owned item, a grid cell (-1 = first free) or a destination index; it
  never sends an item, a price or a total. The original buy and warehouse layouts, which carry a whole client-built item
  or warehouse, are not used.
- `NpcServiceResult`: `{Guid OperationId, uint32 NpcSerial, uint8 Service, uint8 Action, uint16 Status, int32 Ok, int64
  Revision, int64 StorageRevision, text Message}`. `Status` is `NpcServiceStatus` (`NotAvailable` for services Etervale
  does not run, `OutOfReach`, `Rejected`, `Stale`, `NotEnoughGold`, `NoSpace`, `GoldLimit`, `Busy`, `StorageFailed`,
  `Conflict`, `NotOpen`). The state messages a request produces (`ShopCatalog`, `ShopQuotes`, `Warehouse`,
  `TeleportList`) carry the same operation id and precede the result. A teleport also sends `WARPFIELD` with `EParam` =
  1.

The server checks every request against the NPC in the player's field (alive, within 96 units, offering the service),
writes durable changes before applying them and records the operation id with the character, so a retried request
returns its first outcome without charging again.

### Death and restart

Bodies start with `{uint16 Version = 1, uint16 Flags = 0}`.

| Message | Layout after the header | Size |
| --- | --- | --- |
| `RestartRequest` | `Guid OperationId, uint8 Choice (1 field, 2 town, 3 exit), uint8 0, uint16 0` | 32 |
| `RestartResult` | `Guid OperationId, uint8 Status (RestartStatus), uint8 Choice, uint8 ScrollUsed, uint8 0, uint64 ExperienceLost, int64 GoldLost` | 48 |
| `RestartOffer` | `uint8 Flags (1 field restart open, 2 a Resurrection Scroll waives the shares), uint8 0, uint16 0, uint64 ExperienceShare, int64 GoldShare` | 32 |

`RestartStatus`: 0 restarted, 1 not dead, 2 field refused, 3 busy, 4 storage failed, 5 rejected. Rules:
[application.md](application.md#players-worldplayer).

### Quests

Quest bodies start with `{uint16 Version = 1, uint16 Flags = 0}`; text is UTF-8 with a `uint16` byte length (64 bytes,
notices 128). A dialog is `{uint16 Code, uint8 Followup, uint8 0, int32 State, int32 Choice, text Document, Title,
ConfirmLine1, ConfirmLine2, ConfirmDetail, int32 ChoiceCount, (text Choice, text ChoiceDocument)...}` (at most 16
choices); a notice list is `{int32 Count, (uint8 Kind, uint8 0, text)...}` (at most 32). Codes and enums are
`QuestCode`, `QuestAction`, `QuestStatus`, `QuestFollowup`, `QuestNoticeKind`, `QuestTimerKind` (see
[quests.md](quests.md)).

- `QuestRequest` (44 bytes): `{Guid OperationId, uint32 NpcSerial, uint8 Action, uint8 0, uint16 Code, int32 State,
  int32 Choice}`. `NpcSerial` is 0 only for the redistribution offer and the victory dialog of a duel.
- `QuestResult`: `{Guid OperationId, uint8 Status, uint8 Flags (1 ok, 2 class change offered, 4 redistribution
  offered, 8 dialog follows), uint16 0, int64 Revision, text Message, [dialog], notices}`.
- `QuestJournal`: `{int64 Revision, uint16 Code, uint8 Timer, uint8 RedistributionOffered, int32 State, text
  TargetName, int32 Kills, KillsNeeded, int64 TimerMs, int32 WeaponStage, WeaponKillsLeft, int32 ProgressCount,
  (text Title, text Description)..., int32 FinishedCount, lines}` (at most 16 running and 32 finished lines; a
  description names a file under the client's quest board folder).
- `QuestNotices`: `{uint8 Flags (1 dialog follows), uint8 0, uint16 0, notices, [dialog]}`; more than 32 notices
  are sent as several messages, the dialog with the last.

The server answers a request only after a changed character is stored; the presence stream carries the journal and
notices of the tick (kill counts, timers, duel results) to the owner alone. A step that sends the player to or from the
quest arena arrives as the usual field confirmation `WARPFIELD`.

## Server capacity

The original server ran all game logic on one thread under one lock, scanned every connection and monster slot for
visibility, and held 600 suspended worker threads with a shared 1024-slot send queue. Its default cap was 800 players
per process. Etervale keeps the original view ranges and distance-weighted detail and changes the mechanics. Who
receives what, in which order and how often is specified in [server.md](server.md#presence-propagation); the design:

- Fields step in parallel on the tick. They share only immutable catalogs and the thread-safe id allocator.
- Who sees whom is prepared once per field and tick, not per pair: a `PresenceFrame` holds the views of every entity
  with its field-wide descriptor revision, an `EntityGrid` (64-unit planar cells, sorted keys, one binary search per
  column), the index of who targets or owns whom, and the NPC list. Frames are reused tick after tick (a receiver
  pins the frame it encodes from on striped counters), so the tick allocates no views, grids or encode buffers.
- Each receiver's `PresenceEncoder` (on its connection's pump, never on the tick) queries the grid, keeps at most 256
  other entities in view (the nearest, plus its NPCs, its attack target, whoever targets it and its summons; an entity
  already shown stays out to 1.15 times the cut-off), and sends an object only when it changed: spawns and discrete
  changes (motion, HP, target, attack, description) immediately, positions at 5 Hz inside the high-detail range
  (squared `0x48000`, about 543 units) and 2 Hz out to the connect range (squared `0x120000`, about 1086 units); an
  object is forgotten beyond 1.15 times the connect range. Position updates share a budget of 2048 bytes per receiver
  per tick, ranked by how overdue they are (a bounded heap keeps only what the budget can carry), which plays the role
  of the original per-receiver user limit and round-robin cursor.
- Full and motion presence entries and `PLAYERINFO` descriptions are encoded once per frame and entity
  (`SharedPresenceWire`) and copied by every receiver that needs them.
- Movement reports travel through the command queue as values (no closure or task per report); the verdict comes back
  through `IMovementVerdictSink`.
- Sockets use .NET asynchronous I/O (IOCP on Windows) with pooled buffers and coalesced writes; the writer and the
  presence pump wait without allocating.
- Every field's stage collision (`Shared/PristonTale.Geometry`) is loaded at startup, as the original server does: 42
  stages, about 1.93 million collision triangles and 179 MiB, in under a second. Monsters step like the original
  server's next-move check (slide along walls with two side probes, climb 20 units, collide only with other monsters);
  NPCs stop at walls and steps above 10 units; spawns, respawns and drops settle on the floor. The client builds its
  collision from the same `CollisionMesh`.
- SQLite runs in WAL mode with `synchronous=NORMAL`. The rollback journal limited a login burst to a few hundred writes
  per second.

### Measured capacity

All numbers come from one Windows machine (16 logical CPUs, .NET 10, Release, server GC). The tick budget is 50 ms.

The synthetic pipeline (`--benchmark-presence`, [testing.md](testing.md#benchmarks)) runs the real `WorldServer` tick
and one real `PresenceEncoder` per player without sockets: players walk with 5 Hz reports, a quarter fight a monster
beside them, a tenth cast an area skill every three seconds, monsters fight back. Crowded packs everyone into a
500-unit square; spread places players 300 units apart. Encoding runs in parallel after each tick, as the pumps do.

| Players | Layout | Tick p50 / p99 | Encode p50 / p99 (all receivers) | Out per client | Allocated per tick |
| --- | --- | --- | --- | --- | --- |
| 500 | spread | 2.02 / 4.92 ms | 1.79 / 4.80 ms | 3.6 KiB/s (187 B/tick) | 129 KiB |
| 1000 | spread | 3.26 / 35.69 ms | 1.75 / 15.72 ms | 3.8 KiB/s (192 B/tick) | 64 KiB |
| 2000 | spread | 3.98 / 9.66 ms | 2.13 / 3.87 ms | 3.9 KiB/s (200 B/tick) | 82 KiB |
| 5000 | spread | 13.64 / 24.92 ms | 4.44 / 16.36 ms | 4.0 KiB/s (206 B/tick) | 197 KiB |
| 500 | crowded | 2.22 / 15.45 ms | 3.41 / 6.73 ms | 21.3 KiB/s (1.1 KiB/tick) | 26 KiB |
| 1000 | crowded | 4.77 / 16.72 ms | 7.29 / 11.20 ms | 22.2 KiB/s (1.1 KiB/tick) | 45 KiB |
| 2000 | crowded | 7.96 / 44.97 ms | 12.70 / 27.52 ms | 23.6 KiB/s (1.2 KiB/tick) | 135 KiB |
| 5000 | crowded | 10.84 / 38.64 ms | 25.99 / 42.23 ms | 24.4 KiB/s (1.2 KiB/tick) | 292 KiB |

The compact presence entries halved the spread traffic and cut the crowded traffic by about 60%: with the original
play-data entries (84 bytes near, 40 far) the same runs sent 7.6 to 8.4 KiB/s spread and 54.6 to 60.2 KiB/s crowded, and
5000 crowded encoded in 33.25 / 59.79 ms.

Before the shared frames, the crowd cap and the allocation work, the same benchmark measured 1000 crowded at 2.68 /
35.81 ms tick and 34.83 / 116.88 ms encode with 98.7 KiB/s per client and 4.9 MiB allocated per tick, and 5000 crowded
at 16 / 735 ms tick and 455 / 1584 ms encode with 354 KiB/s per client and an 8.6 GiB heap.

The end-to-end load test below was measured with the original play-data entries, before the compact presence entries
(`tools/PristonTale.LoadTest --mode mixed`: half the bots walk, three in ten hunt the nearest
monster with basic attacks, two in ten cast Mechanic Bomb) ran against the imported world on Forest 1 with full stage
collision, the server and every bot on the same machine. Crowded bots start inside a 500-unit square, spread bots on an
80-unit grid; each run measured 60 s after the ramp:

| Bots | Layout | Failures | World tick p50 / p99 | Server CPU | Allocated | Working set | Out per connection |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 500 | crowded | 0 | 2.77 / 6.60 ms | 1.5 cores | 1.9 MiB/s | 594 MiB | 29.1 KiB/s (1.5 KiB/tick) |
| 500 | spread | 0 | 4.96 / 7.14 ms | 2.4 cores | 1.7 MiB/s | 695 MiB | 33.7 KiB/s (1.7 KiB/tick) |
| 1000 | crowded | 0 | 4.31 / 10.10 ms | 2.6 cores | 4.6 MiB/s | 835 MiB | 30.9 KiB/s (1.6 KiB/tick) |
| 1000 | spread | 0 | 4.52 / 9.88 ms | 2.5 cores | 4.4 MiB/s | 890 MiB | 45.0 KiB/s (2.3 KiB/tick) |
| 2000 | crowded | 0 | 11.40 / 25.07 ms | 6.2 cores | 10.4 MiB/s | 1257 MiB | 30.2 KiB/s (1.5 KiB/tick) |
| 2000 | spread | 0 | 12.96 / 28.70 ms | 6.0 cores | 8.6 MiB/s | 1438 MiB | 34.8 KiB/s (1.8 KiB/tick) |
| 5000 | crowded | 0 | 26.29 / 48.87 ms | 9.3 cores | 27.1 MiB/s | 4398 MiB | 18.4 KiB/s (0.9 KiB/tick) |
| 5000 | spread | 0 | 22.93 / 42.93 ms | 9.0 cores | 22.2 MiB/s | 3452 MiB | 15.7 KiB/s (0.8 KiB/tick) |

Before this work the load test's 2000 crowded run allocated 57 to 82 MiB/s. At 5000 bots the load-test process itself
uses most of the remaining cores, so the tick shares the machine; the encoders then fall behind the 20 Hz tick, their
outboxes coalesce, and each client receives fewer, larger updates (the lower bandwidth per connection) while every
event still arrives in order. The many position corrections in these runs come from the bots walking straight through
the stage's walls, not from the server. Reproduce a run with:

```bash
dotnet run --project tools/PristonTale.DevAccounts -c Release --no-build -- --sqlite <copy.db> \
  create-bots cw 5000 bot-pw --class Mechanician --level 20 --field 2 --at 4184,-11016 --spacing 0 \
  --skills 0x01010002:5,0x01010003:3
dotnet run --project Server/PristonTale.Server -c Release --no-build -- --Database:SqlitePath=<copy.db> \
  --Network:Port=10037 --Network:MaxConnectionsPerIp=0 --Server:ReportSeconds=10
dotnet run --project tools/PristonTale.LoadTest -c Release --no-build -- --count 2000 \
  --address tcp://127.0.0.1:10037 --prefix cw --mode mixed --seconds 60 --ramp 100 --server-pid <pid> --report 10
dotnet run --project tests/PristonTale.Tests -c Release --no-build -- \
  --benchmark-presence 500,1000,2000,5000 spread,crowded 100
```

The server logs tick percentiles, process CPU, allocation rate, collections, heap and working set, and bandwidth per
connection every `Server:ReportSeconds` (30 by default).

## How to add an opcode or packet

1. **Code.** Add a constant to `EtervaleOpcodes` in `Binary/PristonOpcodes.cs`, in the range of its area (`0x455401xx`
   owner state, `0x4554011x` inventory, `0x4554012x` progression, `0x4554013x` NPC, `0x4554014x` combat, `0x4554015x`
   removals, `0x4554016x` leave, `0x4554017x` clock, `0x455402xx` skills, `0x455403xx` item details, `0x455404xx`
   usable items, `0x455405xx` quests). Never give an original `0x4847xxxx` or `0x5032xxxx` code a new layout; reuse an
   original message only when its layout stays byte-identical.
2. **Packet type.** Add a record in `Packets/` with `Size` or `HeaderSize` constants, `int Write(Span<byte>)` built on
   `PacketWriter` (returning `Finish()`), and `static Read(ReadOnlySpan<byte>)` built on `PacketReader(packet,
   minimum)`. Start new bodies with `{uint16 Version, uint16 Flags}`, reject version 0, append fields only at the end,
   and bound every count by a `Max*` constant that keeps the packet within 8192 bytes; split longer lists into parts or
   batches.
3. **Server, C→S.** Add a `case` in `ClientSession.HandleAsync`, map the packet to an application input, catch the
   exceptions the call can throw and send a failed result if the client waits for one. Reply with
   `SendLarge(packet.Write, capacity, encrypt: true)`.
4. **Server, S→C presence.** Expose the state on `WorldSnapshot`/`OwnerState` in `Shared/PristonTale.Application`, map
   it in `WireMapping` (or `NpcWireMapping`), emit it from `PresenceEncoder.EncodeLocked`, and call `SendClock` first if
   it belongs to the timeline.
5. **Client.** Presence messages: add a case in `PresenceDecoder.TryHandle` and a `PresenceUpdate` member in
   `Client/PristonTale.Client.Net/Presence`. Request replies: add a case in `GameServerConnection.HandleAsync` and
   complete the pending request by operation id or serial. Requests: add a send method in `GameServerConnection` that
   writes under `_sendGate`.
6. **Tests.** Add a layout round trip next to its siblings (`BinaryProtocolTests`, `SkillWireTests`,
   `NpcServiceTests.ProtocolAsync`) and, for presence, a `WireRoundTrip` case; new test classes are registered in
   `tests/PristonTale.Tests/Program.cs` through `Cases()`. If login or entry changes, run
   `tools/PristonTale.SmokeClient` against a running server; `tools/PristonTale.LoadTest` shares `GameServerConnection`.
7. **Docs.** Update the tables in this file and, for new settings or handlers, [server.md](server.md).

To add a field to an existing versioned message, append it, raise `CurrentVersion`, and make `Read` fill the field only
when the received version has it.

## Pitfalls

- `TransCommand` writes `LParam` before `WParam`; constructor order is `(Code, LParam, WParam, SParam, EParam)`. Most
  original messages are documented by meaning (`WParam` = field), so check the constructor call, not the position.
- Fixed `char[]` fields are Windows-1252. Authored Korean names cannot cross the original layouts; that is why NPC
  messages use UTF-8 text. `PristonText.Strict` throws on unrepresentable text, so the client checks
  `PristonText.IsRepresentable` before sending names and passwords.
- `PacketReader` accepts trailing bytes, so an old reader keeps working when a fixed-size body grows. List bodies
  (`SkillEvents`, `StatusEffects`, `SkillCooldowns`, `CombatEvents`, `Inventory`, ...) check `Remaining == count *
  EntrySize` exactly, so appending anything to them breaks every deployed reader; make readers version-aware first or
  add a new opcode.
- A malformed packet that makes a `Read` throw closes the whole connection (`protocol: ...`), except `ATTACK_DAMAGE`.
- `SkillCastStatus` (wire) and `SkillCastVerdict` (simulation), and `CombatEventKindView` and the client's combat kinds,
  are separate enums cast by value; `SkillWireTests` pins them. Change both sides together.
- `GETPLAYERINFO` is outside the packet flood count but has its own token bucket; a client that requests every serial
  each frame will see descriptions silently dropped. A client must ask again for an object still undescribed (the
  Etervale client does after a second), or a crowd entering view at once never appears.
- Two monsters of one template share a description only while both are wild: a summon's description names its owner.
  Never send a summon's template in `dwAutoCharCode`, and never cache a user-brood description by template.
- A `RECORDDATA` record that fits one chunk declares `Total` = 0, not 1. `RecordDataChunks.Split`,
  `RecordDataAssembler.Add` and `ClientSession.AcknowledgeClientSave` all treat 0 as a single chunk; keep them aligned.
- `SERVER_INFO` uses the login socket's local address; behind NAT an original client would dial the private address.

## Related

- [server.md](server.md): connection lifecycle, packet-to-application mapping, limits and settings.
- [client.md](client.md): client networking, presence consumption and remote world.
- [skills.md](skills.md): skill rules behind `SkillCast`, `SkillEvents` and `StatusEffects`.
- [application.md](application.md): `WorldServer`, snapshots and transactions that produce the outbound messages.
- [world.md](world.md): collision, fields, gates and warps used by movement authority.
- [testing.md](testing.md): running the regression suite.
