using System.Buffers.Binary;
using System.Numerics;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Client.Net.Online;

// Rebuilds the neutral presence model from original packets. Like the original client, an unknown
// serial in PLAYDATAGROUP triggers GETPLAYERINFO; the object is published once its PLAYERINFO description arrives,
// and a changed bUpdateInfo[0] revision requests a fresh description.
internal sealed class PresenceDecoder
{
    private const uint GoldCode = 0x05010100;
    private readonly Dictionary<uint, Tracked> _objects = new();
    // Monster and NPC descriptions are shared by template, like the original client's enemy cache.
    private readonly Dictionary<uint, Description> _templates = new();
    private readonly WorldRenderCatalog _catalog;
    private readonly Action<uint> _requestDescription;
    private readonly Dictionary<int, InventoryItemEntry[]> _inventoryParts = new();
    private OwnerStatusPacket? _status;
    private long _inventoryRevision = -1;
    private long _partsRevision = -1;
    private IReadOnlyList<OwnedItemView> _items = [];
    private long _skillsRevision = -1;
    private IReadOnlyList<OwnedSkillView> _skills = [];
    private long _tick;
    private double? _clockMs;

    public PresenceDecoder(WorldRenderCatalog catalog, Action<uint> requestDescription)
    {
        _catalog = catalog;
        _requestDescription = requestDescription;
    }

    public uint SelfSerial { get; set; }

    public int? FieldId { get; private set; }

    // Decodes one presence packet; returns false for opcodes that are not part of presence.
    public bool TryHandle(ReadOnlySpan<byte> packet, out PresenceUpdate? update)
    {
        var code = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
        update = code switch
        {
            PristonOpcodes.PlayDataGroup => OnPlayDataGroup(packet),
            PristonOpcodes.PlayerInfo => OnPlayerInfo(PlayerInfoPacket.Read(packet)),
            EtervaleOpcodes.RemoveObjects => OnRemoved(RemoveObjectList.Read(packet)),
            EtervaleOpcodes.CombatEvents => OnCombatEvents(CombatEventList.Read(packet)),
            PristonOpcodes.PlayItem => OnGroundItems(GroundItemList.Read(packet)),
            PristonOpcodes.DelItem => OnGroundItemRemoved(GroundItemAction.Read(packet)),
            PristonOpcodes.WarpField => OnWarpField(TransCommand.Read(packet)),
            EtervaleOpcodes.OwnerStatus => OnOwnerStatus(OwnerStatusPacket.Read(packet)),
            EtervaleOpcodes.Inventory => OnInventory(InventoryPartPacket.Read(packet)),
            EtervaleOpcodes.Skills => OnSkills(SkillsPacket.Read(packet)),
            EtervaleOpcodes.PresenceClock => OnPresenceClock(PresenceClockPacket.Read(packet)),
            _ => null
        };
        return update is not null || code is PristonOpcodes.PlayDataGroup or PristonOpcodes.PlayerInfo or EtervaleOpcodes.RemoveObjects
            or EtervaleOpcodes.CombatEvents or PristonOpcodes.PlayItem or PristonOpcodes.DelItem or PristonOpcodes.WarpField
            or EtervaleOpcodes.OwnerStatus or EtervaleOpcodes.Inventory or EtervaleOpcodes.Skills or EtervaleOpcodes.PresenceClock;
    }

    public double? ServerTimeMs => _clockMs;

    // A server without the extension leaves the time unset and the client falls back to arrival time.
    public PresenceUpdate? OnPresenceClock(PresenceClockPacket clock)
    {
        _clockMs = clock.ServerTimeMs;
        return null;
    }

    public PresenceUpdate? OnPlayDataGroup(ReadOnlySpan<byte> packet)
    {
        _tick = BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]);
        var spawns = new List<EntitySpawnInfo>();
        var states = new List<EntityStateInfo>();
        PlayDataGroup.Read(packet, entry =>
        {
            var code = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            if (code == PristonOpcodes.TransPlayData)
            {
                Apply(TransPlayData.Read(entry), spawns, states);
            }
            else if (code == PristonOpcodes.PlayData1)
            {
                Apply(PlayDataCoarse.Read(entry), states);
            }
        });
        return spawns.Count == 0 && states.Count == 0 ? null : Update(spawns, states) with { ServerTimeMs = _clockMs };
    }

    private void Apply(TransPlayData data, List<EntitySpawnInfo> spawns, List<EntityStateInfo> states)
    {
        var buffer = data.Buffers[^1];
        if (!_objects.TryGetValue(data.ObjectSerial, out var tracked))
        {
            tracked = new Tracked();
            _objects.Add(data.ObjectSerial, tracked);
        }

        if (data.ObjectSerial == SelfSerial && data.FieldCode >= 0)
        {
            FieldId = data.FieldCode;
        }

        tracked.Hp = data.Hp;
        tracked.MaxHp = data.MaxHp;
        tracked.Target = data.Target;
        tracked.Sequence = (uint)data.StartPosi;
        tracked.Position = Position(buffer.X, buffer.Y, buffer.Z);
        tracked.Facing = PristonUnits.FromAngle(buffer.AngleY);
        tracked.Motion = MoveState(buffer.Action);
        tracked.SampleTimeMs = _clockMs;
        tracked.HasState = true;
        if (tracked.Description is null && data.AutoCharCode != 0 && _templates.TryGetValue(data.AutoCharCode, out var shared)
            && shared.Revision == data.DescriptorRevision)
        {
            tracked.Description = shared;
        }

        if (tracked.Description is null || tracked.Description.Revision != data.DescriptorRevision)
        {
            if (tracked.RequestedRevision != data.DescriptorRevision)
            {
                tracked.RequestedRevision = data.DescriptorRevision;
                _requestDescription(data.ObjectSerial);
            }
        }

        Publish(data.ObjectSerial, tracked, spawns, states);
    }

    private void Apply(PlayDataCoarse data, List<EntityStateInfo> states)
    {
        if (!_objects.TryGetValue(data.ObjectSerial, out var tracked) || !tracked.HasState)
        {
            // A coarse entry never introduces an object; its next detailed entry does.
            return;
        }

        tracked.Position = Position(data.X, data.Y, data.Z);
        tracked.Facing = PristonUnits.FromAngle(data.AngleY);
        tracked.Motion = MoveState(data.Action);
        tracked.SampleTimeMs = _clockMs;
        if (tracked.Published)
        {
            states.Add(State(data.ObjectSerial, tracked));
        }
    }

    private void Publish(uint serial, Tracked tracked, List<EntitySpawnInfo> spawns, List<EntityStateInfo> states)
    {
        if (tracked.Description is null)
        {
            return;
        }

        if (!tracked.Published)
        {
            tracked.Published = true;
            spawns.Add(Spawn(serial, tracked));
        }
        else
        {
            states.Add(State(serial, tracked));
        }
    }

    public PresenceUpdate? OnPlayerInfo(PlayerInfoPacket packet)
    {
        var serial = packet.ObjectSerial;
        if (!_objects.TryGetValue(serial, out var tracked))
        {
            return null;
        }

        var character = packet.Character;
        var description = new Description(character.UpdateInfo[0], Kind(character.State), character);
        tracked.Description = description;
        var template = character.Temp[CharacterInfo.TempTemplateCode];
        var scale = BitConverter.UInt32BitsToSingle(character.Temp[CharacterInfo.TempModelScale]);
        if (description.Kind is RemoteEntityKind.Monster or RemoteEntityKind.Npc && template != 0)
        {
            _templates[template] = description;
        }
        if (description.Kind == RemoteEntityKind.Monster)
        {
            _catalog.Register(new MonsterRenderInfo(template, character.ModelName, scale, character.Name, character.Level));
        }
        else if (description.Kind == RemoteEntityKind.Npc)
        {
            _catalog.Register(new NpcRenderInfo(template, character.ModelName, scale, character.Name,
                (NpcServices)character.Temp[CharacterInfo.TempNpcServices], (int)character.CharSoundCode));
        }

        if (!tracked.HasState)
        {
            return null;
        }

        // A new description is a full refresh, which the scene applies as a spawn of a known id.
        tracked.Published = true;
        return Update([Spawn(serial, tracked)], []);
    }

    public PresenceUpdate OnRemoved(uint[] serials)
    {
        var removed = new List<uint>(serials.Length);
        foreach (var serial in serials)
        {
            if (_objects.Remove(serial, out var tracked) && tracked.Published)
            {
                removed.Add(serial);
            }
        }

        return Update([], [], removed);
    }

    public PresenceUpdate OnCombatEvents(CombatEventEntry[] entries)
    {
        var events = new List<CombatEventInfo>(entries.Length);
        foreach (var entry in entries)
        {
            events.Add(new CombatEventInfo(entry.Attacker, entry.Target, (RemoteCombatEventKind)entry.Kind, entry.DurationMs, entry.HitAtMs,
                entry.Damage, (entry.Flags & CombatEventFlags.Critical) != 0, (entry.Flags & CombatEventFlags.Killed) != 0, entry.Resources,
                entry.SoundCode, Position(entry.X, entry.Y, entry.Z), PristonUnits.FromAngle(entry.Angle)));
        }

        return Update([], [], events: events) with { ServerTimeMs = _clockMs };
    }

    public PresenceUpdate OnGroundItems(GroundItemEntry[] items)
    {
        var added = new List<GroundDropView>(items.Length);
        foreach (var item in items)
        {
            var gold = item.ItemCode == GoldCode;
            var key = gold ? "GG101" : LegacyItemCode.TryFormat(item.ItemCode, out var code) ? code : "";
            added.Add(new GroundDropView(GroundDropHandle.ToGuid(item.Handle), item.ItemCode, key, item.Name, item.Amount, gold,
                Position(item.X, item.Y, item.Z)) { Handle = item.Handle });
        }

        return Update([], []) with { GroundAdded = added };
    }

    public PresenceUpdate OnGroundItemRemoved(GroundItemAction action) =>
        Update([], []) with { GroundRemoved = [GroundDropHandle.ToGuid(action.Handle)] };

    // WARPFIELD {WParam = FieldCode, SParam = x, LParam = z}; Etervale sets EParam = 1 when it rejects a report.
    public PresenceUpdate OnWarpField(TransCommand command)
    {
        FieldId = command.WParam;
        _objects.TryGetValue(SelfSerial, out var self);
        var y = self?.Position.Y ?? 0f;
        var position = new Vector3(PristonUnits.FromFixed(command.SParam), y, PristonUnits.FromFixed(command.LParam));
        if (self is not null)
        {
            self.Position = position;
        }

        return Update([], []) with { Warp = new WarpInfo(command.WParam, position, command.EParam == 1) };
    }

    public PresenceUpdate? OnOwnerStatus(OwnerStatusPacket status)
    {
        _status = status;
        return OwnerUpdate();
    }

    public PresenceUpdate? OnInventory(InventoryPartPacket part)
    {
        if (part.Revision != _partsRevision)
        {
            _inventoryParts.Clear();
            _partsRevision = part.Revision;
        }

        _inventoryParts[part.Part] = part.Items.ToArray();
        if (_inventoryParts.Count < part.Parts)
        {
            return null;
        }

        _items = _inventoryParts.OrderBy(pair => pair.Key).SelectMany(pair => pair.Value).Select(Item).ToArray();
        _inventoryRevision = part.Revision;
        _inventoryParts.Clear();
        return OwnerUpdate();
    }

    public PresenceUpdate? OnSkills(SkillsPacket skills)
    {
        _skills = skills.Skills.Select(skill => new OwnedSkillView(skill.Code, skill.Rank)).ToArray();
        _skillsRevision = skills.Revision;
        return OwnerUpdate();
    }

    private PresenceUpdate? OwnerUpdate()
    {
        var status = _status;
        if (status is null || status.InventoryRevision != _inventoryRevision || status.SkillsRevision != _skillsRevision)
        {
            return null;
        }

        var owner = new OwnerState(status.Level, status.Experience, status.LevelStart, status.NextLevel, status.Money, status.Hp, status.MaxHp,
            status.Mp, status.MaxMp, status.Sp, status.MaxSp, status.StatPoints, status.SkillPoints, status.Strength, status.Spirit, status.Talent,
            status.Agility, status.Health, status.DamageMin, status.DamageMax, status.Defence, status.Absorption, status.InventoryRevision, _items)
        {
            AttackRange = status.AttackRange,
            AttackRating = status.AttackRating,
            MoveSpeed = status.MoveSpeed,
            ProgressionRevision = status.ProgressionRevision,
            SkillsRevision = status.SkillsRevision,
            Skills = _skills
        };
        return Update([], []) with { Owner = owner };
    }

    private static OwnedItemView Item(InventoryItemEntry entry) => new(entry.InstanceId, entry.Code, entry.Name, (ItemKind)entry.Kind, entry.Count,
        entry.Anchor, entry.Slot, entry.Width, entry.Height, LegacyItemCode.TryFormat(entry.Code, out var key) ? key : "");

    private PresenceUpdate Update(List<EntitySpawnInfo> spawns, List<EntityStateInfo> states, List<uint>? removed = null, List<CombatEventInfo>? events = null) =>
        new(_tick, spawns, states, removed ?? [], events ?? [], FieldId);

    private EntitySpawnInfo Spawn(uint serial, Tracked tracked)
    {
        var description = tracked.Description!;
        var character = description.Character;
        PlayerAppearanceInfo? player = null;
        if (description.Kind == RemoteEntityKind.Player)
        {
            var faceAndHair = character.Temp[CharacterInfo.TempFaceAndHair];
            player = new PlayerAppearanceInfo(character.Name, (int)character.JobCode, (int)character.ChangeJob, (int)(faceAndHair & 0xFF),
                (int)(faceAndHair >> 8 & 0xFF), character.ModelName, character.ModelName2, character.Level, Key(character.Temp[CharacterInfo.TempWeaponCode]),
                Key(character.Temp[CharacterInfo.TempArmorCode]), Key(character.Temp[CharacterInfo.TempOffHandCode]),
                (character.Temp[CharacterInfo.TempFlags] & CharacterInfo.FlagInVillage) != 0);
        }

        return new EntitySpawnInfo(serial, description.Kind, serial == SelfSerial, tracked.Position, tracked.Facing, tracked.Motion, tracked.Hp,
            tracked.MaxHp, player, description.Kind == RemoteEntityKind.Player ? 0 : character.Temp[CharacterInfo.TempTemplateCode], character.MoveSpeed,
            AttackRate(character), tracked.Target, tracked.Sequence, tracked.SampleTimeMs, character.Name ?? "", character.Level);
    }

    private static EntityStateInfo State(uint serial, Tracked tracked)
    {
        var character = tracked.Description!.Character;
        return new EntityStateInfo(serial, tracked.Position, tracked.Facing, tracked.Motion, tracked.Hp, character.MoveSpeed, AttackRate(character),
            tracked.Target, tracked.Sequence, tracked.SampleTimeMs);
    }

    private static float AttackRate(CharacterInfo character)
    {
        var rate = BitConverter.UInt32BitsToSingle(character.Temp[CharacterInfo.TempAttackRate]);
        return float.IsFinite(rate) && rate > 0f ? rate : 1f;
    }

    private static string Key(uint code) => code != 0 && LegacyItemCode.TryFormat(code, out var key) ? key : "";

    private static Vector3 Position(int x, int y, int z) => new(PristonUnits.FromFixed(x), PristonUnits.FromFixed(y), PristonUnits.FromFixed(z));

    private static RemoteEntityKind Kind(int state) => state switch
    {
        CharacterState.User => RemoteEntityKind.Player,
        CharacterState.Enemy => RemoteEntityKind.Monster,
        CharacterState.Npc => RemoteEntityKind.Npc,
        _ => RemoteEntityKind.Unspecified
    };

    public static RemoteMoveState MoveState(short action) => action switch
    {
        MotionStateCode.Stand => RemoteMoveState.Idle,
        MotionStateCode.Walk => RemoteMoveState.Walking,
        MotionStateCode.Run => RemoteMoveState.Running,
        MotionStateCode.Attack => RemoteMoveState.Attack,
        MotionStateCode.Damage => RemoteMoveState.Damage,
        MotionStateCode.Dead => RemoteMoveState.Dead,
        MotionStateCode.Skill => RemoteMoveState.Skill,
        MotionStateCode.Eat => RemoteMoveState.Eat,
        _ => RemoteMoveState.Unspecified
    };

    private sealed record Description(byte Revision, RemoteEntityKind Kind, CharacterInfo Character);

    private sealed class Tracked
    {
        public Description? Description { get; set; }
        public int RequestedRevision { get; set; } = -1;
        public bool HasState { get; set; }
        public bool Published { get; set; }
        public Vector3 Position { get; set; }
        public float Facing { get; set; }
        public RemoteMoveState Motion { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public uint Target { get; set; }
        public ulong Sequence { get; set; }
        public double? SampleTimeMs { get; set; }
    }
}
