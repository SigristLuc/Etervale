using System.Buffers.Binary;
using System.Numerics;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Client.Net.Online;

// Rebuilds the neutral presence model from original packets. Like the original client, an unknown
// serial in PlayDataGroup triggers GetPlayerInfo; the object is published once its PlayerInfo description arrives,
// and a changed descriptor revision (TransPlayData.DescriptorRevision) requests a fresh description.
internal sealed class PresenceDecoder
{
    private const uint GoldCode = 0x05010100;
    private const long DescriptionRetryMs = 1000;
    // The receive loop decodes; the connection's timer retries descriptions.
    private readonly object _gate = new();
    private readonly Dictionary<uint, Tracked> _objects = new();
    // Monster and NPC descriptions are shared by template, like the original client's enemy cache.
    private readonly Dictionary<uint, Description> _templates = new();
    private readonly WorldRenderCatalog _catalog;
    private readonly Action<uint> _requestDescription;
    private readonly Dictionary<int, InventoryItemEntry[]> _inventoryParts = new();
    private readonly OwnerItemDecoder _ownerItems = new();
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
        lock (_gate)
        {
            return TryHandleLocked(packet, out update);
        }
    }

    // Asks again for the descriptions whose answer did not come within a second. The connection calls it about once a
    // second: an object that stands still sends no entries that would trigger the request again.
    public void RetryDescriptions()
    {
        lock (_gate)
        {
            foreach (var (serial, tracked) in _objects)
            {
                if (tracked.HasState && (tracked.Description is null || tracked.Description.Revision != tracked.Revision))
                {
                    RequestDescription(serial, tracked, tracked.Revision);
                }
            }
        }
    }

    private bool TryHandleLocked(ReadOnlySpan<byte> packet, out PresenceUpdate? update)
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
            EtervaleOpcodes.SkillEvents => OnSkillEvents(SkillEventList.Read(packet)),
            EtervaleOpcodes.StatusEffects => OnStatusEffects(StatusEffectList.Read(packet)),
            EtervaleOpcodes.SkillCooldowns => OnSkillCooldowns(SkillCooldownsPacket.Read(packet)),
            EtervaleOpcodes.SkillCastResult => OnSkillCastResult(SkillCastResultPacket.Read(packet)),
            EtervaleOpcodes.ItemDetails => OnItemDetails(ItemDetailsPartPacket.Read(packet)),
            EtervaleOpcodes.OwnerItemStatus => OnOwnerItemStatus(OwnerItemStatusPacket.Read(packet)),
            EtervaleOpcodes.PremiumTimers => OnPremiumTimers(PremiumTimersPacket.Read(packet)),
            EtervaleOpcodes.ItemEffects => OnItemEffects(ItemEffectsPacket.Read(packet)),
            EtervaleOpcodes.QuestJournal => Update([], []) with { Quest = QuestWireReader.ToView(QuestJournalPacket.Read(packet)) },
            EtervaleOpcodes.QuestNotices => OnQuestNotices(QuestNoticesPacket.Read(packet)),
            _ => null
        };
        return update is not null || code is PristonOpcodes.PlayDataGroup or PristonOpcodes.PlayerInfo or EtervaleOpcodes.RemoveObjects
            or EtervaleOpcodes.CombatEvents or PristonOpcodes.PlayItem or PristonOpcodes.DelItem or PristonOpcodes.WarpField
            or EtervaleOpcodes.OwnerStatus or EtervaleOpcodes.Inventory or EtervaleOpcodes.Skills or EtervaleOpcodes.PresenceClock
            or EtervaleOpcodes.SkillEvents or EtervaleOpcodes.StatusEffects or EtervaleOpcodes.SkillCooldowns or EtervaleOpcodes.SkillCastResult
            or EtervaleOpcodes.ItemDetails or EtervaleOpcodes.OwnerItemStatus
            or EtervaleOpcodes.PremiumTimers or EtervaleOpcodes.ItemEffects or EtervaleOpcodes.QuestJournal or EtervaleOpcodes.QuestNotices;
    }

    private PresenceUpdate OnQuestNotices(QuestNoticesPacket packet) =>
        Update([], []) with { QuestNotices = QuestWireReader.ToNotices(packet.Notices), QuestPrompt = QuestWireReader.ToPrompt(packet.Prompt) };

    public PresenceUpdate OnSkillEvents(SkillEventEntry[] entries)
    {
        var events = new SkillEventInfo[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            events[index] = new SkillEventInfo(entry.Caster, entry.Target, entry.SkillCode, entry.Phase, entry.Rank, entry.Flags, entry.DurationMs,
                entry.HitAtMs, Position(entry.X, entry.Y, entry.Z), PristonUnits.FromAngle(entry.Angle), entry.TargetCount, entry.Radius);
        }

        return Update([], []) with { SkillEvents = events, ServerTimeMs = _clockMs };
    }

    public PresenceUpdate OnStatusEffects(StatusEffectEntry[] entries)
    {
        var statuses = new StatusEffectInfo[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            statuses[index] = new StatusEffectInfo(entry.Entity, entry.Source, entry.StatusCode, entry.Op, entry.Stacks, entry.SourceKind,
                entry.Control, entry.Flags, entry.DurationMs, entry.RemainingMs, entry.Value);
        }

        return Update([], []) with { StatusEffects = statuses, ServerTimeMs = _clockMs };
    }

    public PresenceUpdate OnSkillCooldowns(SkillCooldownsPacket packet)
    {
        var cooldowns = new SkillCooldownInfo[packet.Cooldowns.Count];
        for (var index = 0; index < cooldowns.Length; index++)
        {
            var entry = packet.Cooldowns[index];
            cooldowns[index] = new SkillCooldownInfo(entry.SkillCode, (int)entry.RemainingMs, (int)entry.DurationMs);
        }

        return Update([], []) with { Cooldowns = new SkillCooldownSnapshot(_clockMs, cooldowns) };
    }

    public PresenceUpdate OnSkillCastResult(SkillCastResultPacket result) => Update([], []) with
    {
        CastResult = new SkillCastResultInfo(result.Sequence, result.SkillCode, result.Status, result.Rank, (int)result.CooldownMs, (int)result.RemainingMs,
            result.Mana, result.Stamina)
    };

    public PresenceUpdate OnPremiumTimers(PremiumTimersPacket packet)
    {
        var timers = new List<PremiumTimerInfo>(packet.Timers.Count);
        foreach (var entry in packet.Timers)
        {
            if (Enum.IsDefined((PremiumEffectKind)entry.Kind))
            {
                timers.Add(new PremiumTimerInfo((PremiumEffectKind)entry.Kind, entry.Value, entry.RemainingSeconds, entry.DurationSeconds));
            }
        }

        return Update([], []) with { PremiumTimers = new PremiumTimerSnapshot(packet.Revision, timers) };
    }

    public PresenceUpdate OnItemEffects(ItemEffectsPacket packet)
    {
        var effects = new ItemEffectInfo[packet.Effects.Count];
        for (var index = 0; index < effects.Length; index++)
        {
            var entry = packet.Effects[index];
            effects[index] = new ItemEffectInfo(entry.Entity, entry.ItemCode, (ItemEffectKind)entry.Effect, Position(entry.X, entry.Y, entry.Z), entry.Variant);
        }

        return Update([], []) with { ItemEffects = effects, ServerTimeMs = _clockMs };
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

        tracked.Revision = data.DescriptorRevision;
        if (tracked.Description is null || tracked.Description.Revision != data.DescriptorRevision)
        {
            RequestDescription(data.ObjectSerial, tracked, data.DescriptorRevision);
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

    // One GETPLAYERINFO per revision, repeated after a second when its answer does not come: the server drops requests
    // beyond its rate bucket (a crowd entering view at once), and an object without a description is never shown.
    private void RequestDescription(uint serial, Tracked tracked, int revision)
    {
        var now = Environment.TickCount64;
        if (tracked.RequestedRevision == revision && now - tracked.RequestedAtMs < DescriptionRetryMs)
        {
            return;
        }

        tracked.RequestedRevision = revision;
        tracked.RequestedAtMs = now;
        _requestDescription(serial);
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
        // A summon's description names its owner, so it never stands for the wild monsters of its template.
        if (description.Kind is RemoteEntityKind.Monster or RemoteEntityKind.Npc && template != 0 && character.Brood != (uint)MonsterBrood.User)
        {
            _templates[template] = description;
        }
        if (description.Kind == RemoteEntityKind.Monster)
        {
            _catalog.Register(new MonsterRenderInfo(template, character.ModelName, scale, character.Name, character.Level,
                (MonsterBrood)character.Brood, character.ArrowPosiX, character.ArrowPosiY));
        }
        else if (description.Kind == RemoteEntityKind.Npc)
        {
            _catalog.Register(new NpcRenderInfo(template, character.ModelName, scale, character.Name,
                new NpcServiceSet(character.Temp[CharacterInfo.TempNpcServices]), (int)character.CharSoundCode, character.ArrowPosiX,
                character.ArrowPosiY));
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

    public PresenceUpdate? OnItemDetails(ItemDetailsPartPacket part)
    {
        _ownerItems.OnDetails(part);
        return null;
    }

    public PresenceUpdate? OnOwnerItemStatus(OwnerItemStatusPacket status)
    {
        _ownerItems.OnStatus(status);
        return null;
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

        _items = _ownerItems.Attach(part.Revision, _inventoryParts.OrderBy(pair => pair.Key).SelectMany(pair => pair.Value).Select(Item).ToArray());
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
            Tier = status.Tier,
            EliteSkillPoints = status.EliteSkillPoints,
            Skills = _skills
        };
        return Update([], []) with { Owner = _ownerItems.Apply(owner) };
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
                (character.Temp[CharacterInfo.TempFlags] & CharacterInfo.FlagInVillage) != 0)
            {
                HelperPet = (int)(character.Temp[CharacterInfo.TempFlags] >> CharacterInfo.HelperPetShift & 0xF),
                Phoenix = (character.Temp[CharacterInfo.TempFlags] & CharacterInfo.FlagPhoenix) != 0,
                BigHead = (int)(character.Temp[CharacterInfo.TempFlags] >> CharacterInfo.BigHeadShift & 0x3)
            };
        }

        var summon = description.Kind == RemoteEntityKind.Monster && character.Brood == (uint)MonsterBrood.User;
        return new EntitySpawnInfo(serial, description.Kind, serial == SelfSerial, tracked.Position, tracked.Facing, tracked.Motion, tracked.Hp,
            tracked.MaxHp, player, description.Kind == RemoteEntityKind.Player ? 0 : character.Temp[CharacterInfo.TempTemplateCode], character.MoveSpeed,
            AttackRate(character), tracked.Target, tracked.Sequence, tracked.SampleTimeMs, character.Name ?? "", character.Level)
        {
            OwnerId = summon ? character.Temp[CharacterInfo.TempOwnerSerial] : 0,
            ModelScale = summon ? BitConverter.UInt32BitsToSingle(character.Temp[CharacterInfo.TempInstanceScale]) : 0f
        };
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
        public long RequestedAtMs { get; set; }
        public byte Revision { get; set; }
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
