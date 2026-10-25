using System.Numerics;
using System.Runtime.InteropServices;
using PristonTale.Application.World;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Server.Net;

// Turns one subscriber's share of a field snapshot into presence messages. It keeps the original's view ranges and
// distance-weighted detail, but queries the field grid instead of scanning every slot, sends an entity only when it
// changed (a full entry for spawns and discrete changes, a motion entry for movement), and lets the near ring update
// faster than the far ring.
internal sealed partial class PresenceEncoder
{
    // Squared view ranges in world units: connect 0x120000, high detail 0x48000, ground items 0xC8000.
    public static readonly float ViewRadius = MathF.Sqrt(0x120000);
    private static readonly float ForgetRadius = ViewRadius * 1.15f;
    private static readonly float NearRadius = MathF.Sqrt(0x48000);
    private static readonly float ItemRadius = MathF.Sqrt(0xC8000);
    // 5 Hz inside the high-detail range and 2 Hz out to the connect range; the original relayed players at about 2.2 Hz and monsters at 1.1 Hz.
    private const uint NearIntervalTicks = 4;
    private const uint FarIntervalTicks = 10;
    // Like the original per-receiver user limit, a crowd cannot exceed a per-receiver movement budget (about 40 KiB/s at 20 Hz).
    // Spawns and discrete changes always go out; position-only updates are ranked by how overdue they are.
    private const int MovementBudgetBytes = 2048;
    private const uint TickIntervalMs = 1000 / WorldServer.TickHz;

    private readonly uint _self;
    private readonly WorldDataHolder _data;
    private readonly object _gate = new();
    private readonly Dictionary<uint, Known> _known = new();
    private readonly Dictionary<Guid, GroundDropView> _drops = new();
    private readonly List<int> _candidates = new();
    // The entities a receiver may show at once; the nearest win in a denser crowd.
    public const int MaxVisible = 256;
    private readonly VisibleSet _visible = new(MaxVisible);
    private readonly List<int> _related = new();
    private readonly HashSet<int> _essential = new();
    private readonly Func<int, bool> _isShown;
    private readonly MotionCandidates _pending = new(MovementBudgetBytes / PresenceWire.MotionEntrySize(0));
    private readonly HashSet<Guid> _visibleDrops = new();
    private readonly List<Guid> _goneDrops = new();
    private readonly List<uint> _removed = new();
    // Entities this receiver started seeing in the snapshot being encoded; their running statuses follow.
    private readonly List<uint> _spawned = new();
    private readonly List<CombatEventEntry> _events = new();
    private readonly List<SkillEventEntry> _skillEvents = new();
    private readonly List<StatusEffectEntry> _statusEvents = new();
    private readonly List<GroundItemEntry> _newDrops = new();
    private readonly byte[] _scratch = new byte[PristonFrameCodec.MaxFrameSize];
    private readonly byte[] _group = new byte[PristonFrameCodec.MaxFrameSize];
    private int? _field;
    private OwnerState? _owner;
    private long _inventoryRevision = -1;
    private long _skillsRevision = -1;
    private uint _groupCounter;
    private uint _clockTick;
    private bool _clockPending;
    private uint _pass;
    private SharedPresenceWire? _wire;
    private Simulation.Progression.RestartOffer? _restartOffer;
    private PresenceFrame? _heldFrame;
    private EntityView[] _describeViews = [];
    private EntityGrid? _describeGrid;
    private EntityView[] _entities = [];

    public PresenceEncoder(uint self, WorldDataHolder data)
    {
        _self = self;
        _data = data;
        _isShown = index => _known.ContainsKey(_entities[index].EntityId);
    }

    public EntityKindView? KindOf(uint serial)
    {
        lock (_gate)
        {
            return _known.TryGetValue(serial, out var known) ? known.Kind : null;
        }
    }

    public Vector3? SelfPosition
    {
        get
        {
            lock (_gate)
            {
                return _known.TryGetValue(_self, out var known) ? known.Position : null;
            }
        }
    }

    public int? Field
    {
        get
        {
            lock (_gate)
            {
                return _field;
            }
        }
    }

    // The description of an entity from the last encoded snapshot (its frame stays pinned until the next one); null when
    // the serial is not in this subscriber's view.
    public EntityDescriptionPacket? Describe(uint serial)
    {
        EntityView view;
        byte revision;
        lock (_gate)
        {
            if (_describeGrid is null || !_known.TryGetValue(serial, out var known) || !_describeGrid.TryGetIndex(serial, out var index))
            {
                return null;
            }

            view = _describeViews[index];
            revision = known.Revision;
        }

        return DescriptionPacket(view, revision, _data.Current);
    }

    // Writes the description into destination and returns its length, 0 when the serial is not in view. Every receiver
    // asking about an entity of the same frame gets the same bytes, so they are encoded once per frame.
    public int WriteDescription(uint serial, Span<byte> destination)
    {
        lock (_gate)
        {
            if (_describeGrid is null || !_known.TryGetValue(serial, out var known) || !_describeGrid.TryGetIndex(serial, out var index))
            {
                return 0;
            }

            ref readonly var view = ref _describeViews[index];
            if (_heldFrame is { } frame && view.Revision != 0 && ReferenceEquals(frame.Views, _describeViews))
            {
                var shared = frame.Attachment(SharedPresenceWire.Create).Description(index, view, _data.Current);
                shared.CopyTo(destination);
                return shared.Length;
            }

            return DescriptionPacket(view, known.Revision, _data.Current).Write(destination);
        }
    }

    internal static EntityDescriptionPacket DescriptionPacket(in EntityView view, byte revision, WorldData data) => WireMapping.Describe(view, revision, data);

    // The connection ended: the last snapshot's frame is no longer needed for descriptions.
    public void Close()
    {
        lock (_gate)
        {
            HoldForDescriptions(null, [], null);
        }
    }

    private void HoldForDescriptions(PresenceFrame? frame, EntityView[] views, EntityGrid? grid)
    {
        _heldFrame?.Release(_self);
        _heldFrame = frame;
        _describeViews = views;
        _describeGrid = grid;
    }

    public void Encode(in WorldSnapshot snapshot, IPacketSink connection)
    {
        lock (_gate)
        {
            EncodeLocked(snapshot, connection);
        }
    }

    private void EncodeLocked(in WorldSnapshot snapshot, IPacketSink connection)
    {
        var grid = snapshot.Grid ?? EntityGrid.Build(snapshot.Entities);
        if (!grid.TryGetIndex(_self, out var selfIndex))
        {
            // No own entity (a snapshot whose frame the field already reused): its reliable events still go out.
            SendEventsOnly(snapshot, connection);
            return;
        }

        var self = snapshot.Entities[selfIndex];
        var field = snapshot.FieldId ?? 0;
        // A frame is pinned again for the descriptions asked about this snapshot; a hand-built snapshot is kept as is.
        if (snapshot.Frame is not { } described || described.TryPin(snapshot.FrameGeneration, _self))
        {
            HoldForDescriptions(snapshot.Frame, snapshot.Entities, grid);
        }

        if (_field != field)
        {
            if (_field is not null)
            {
                ForgetAll(connection);
            }

            _field = field;
            // WARPFIELD {WParam = FieldCode, SParam = x, LParam = z}: confirms the authoritative field and position.
            SendCommand(connection, new TransCommand(PristonOpcodes.WarpField, PristonUnits.ToFixed(self.Position.Z), field,
                PristonUnits.ToFixed(self.Position.X)), encrypt: true);
        }

        var tick = snapshot.ServerTick;
        var combat = snapshot.Events.AsSpan();
        var skills = snapshot.SkillEvents.AsSpan();
        var statuses = snapshot.StatusEvents.AsSpan();
        var items = snapshot.ItemEffects.AsSpan();
        // Events of earlier ticks this receiver fell behind on go first, each on its own tick of the timeline.
        foreach (var segment in snapshot.EventSegments ?? [])
        {
            if (segment.Tick == tick)
            {
                break;
            }

            _clockTick = segment.Tick;
            _clockPending = true;
            SendEvents(combat[..segment.Combat], connection);
            SendSkillEvents(skills[..segment.Skills], connection);
            SendStatusEvents(statuses[..segment.Statuses], snapshot, connection, withSpawned: false);
            SendItemEffects(items[..segment.Items], connection);
            combat = combat[segment.Combat..];
            skills = skills[segment.Skills..];
            statuses = statuses[segment.Statuses..];
            items = items[segment.Items..];
        }

        _clockTick = tick;
        _clockPending = true;
        // Entries every receiver of this frame shares are encoded once by whichever encoder needs them first.
        _wire = snapshot.Frame is { } frame && ReferenceEquals(frame.Views, snapshot.Entities)
            ? frame.Attachment(SharedPresenceWire.Create)
            : null;
        _entities = snapshot.Entities;
        _pass++;
        var group = new PresenceEntriesWriter(_group, ++_groupCounter);
        _removed.Clear();
        _spawned.Clear();
        WriteEntity(ref group, connection, selfIndex, tick, field, near: true);

        _candidates.Clear();
        _pending.Clear();
        _visible.Clear();
        _essential.Clear();
        // Entities a receiver must not lose in a crowd: NPCs, its attack target, whoever targets it and its summons.
        _related.Clear();
        _related.AddRange(grid.Npcs);
        grid.Related(_self, _related);
        if (self.AttackTargetId != 0 && grid.TryGetIndex(self.AttackTargetId, out var targetIndex))
        {
            _related.Add(targetIndex);
        }

        foreach (var index in _related)
        {
            if (_essential.Add(index))
            {
                ConsiderVisible(snapshot, field, index, self.Position, essential: true);
            }
        }

        grid.Query(self.Position, CrowdRadius(grid, self.Position), _candidates);
        foreach (var index in _candidates)
        {
            if (!_essential.Contains(index))
            {
                ConsiderVisible(snapshot, field, index, self.Position, essential: false);
            }
        }

        _visible.Trim(_isShown);
        foreach (var (index, _) in _visible.Entries)
        {
            ref readonly var view = ref snapshot.Entities[index];
            var distanceSq = PlanarDistanceSq(view.Position, self.Position);
            var near = distanceSq <= NearRadius * NearRadius;
            if (WriteEntity(ref group, connection, index, tick, field, near) is { } age)
            {
                // Overdue ratio: the wanted interval grows with distance, and every waiting entity keeps gaining
                // priority, so a crowd is served round-robin like the classic server's rotating send cursor.
                var wanted = NearIntervalTicks + MathF.Sqrt(distanceSq) / 50f;
                _pending.Add(index, age / wanted, near);
            }
        }

        var budget = MovementBudgetBytes;
        foreach (var (index, _, near) in _pending.Ordered())
        {
            var size = PresenceWire.MotionEntrySize(_entities[index].EntityId);
            if (budget < size)
            {
                break;
            }

            budget -= size;
            WriteMotion(ref group, connection, index, tick);
        }

        foreach (var (serial, known) in _known)
        {
            if (known.Pass != _pass)
            {
                _removed.Add(serial);
            }
        }

        foreach (var serial in _removed)
        {
            _known.Remove(serial);
        }

        FlushGroup(ref group, connection);
        _wire = null;
        _entities = [];
        SendRemovals(connection);
        SendEvents(combat, connection);
        SendSkillEvents(skills, connection);
        SendStatusEvents(statuses, snapshot, connection, withSpawned: true);
        SendItemEffects(items, connection);
        SendDrops(snapshot, self.Position, connection);
        SendOwner(snapshot.Owner, connection);
        SendPremium(snapshot.Owner, connection);
        SendCooldowns(snapshot, connection);
        SendRestartOffer(snapshot.Restart, connection);
        if (snapshot.Quest is not null)
        {
            SendQuest(snapshot.Quest, connection);
        }
    }

    // A dead receiver learns what standing up would cost whenever that changes.
    private void SendRestartOffer(Simulation.Progression.RestartOffer? offer, IPacketSink connection)
    {
        if (offer == _restartOffer)
        {
            return;
        }

        _restartOffer = offer;
        if (offer is { } current)
        {
            var packet = new RestartOfferPacket(current.FieldOpen, current.Scroll, current.ExperienceShare, current.GoldShare);
            connection.Send(_scratch.AsSpan(0, packet.Write(_scratch)));
        }
    }

    // Its own method: a lambda capturing the connection in EncodeLocked would allocate a closure on every encode.
    private void SendQuest(QuestUpdate quest, IPacketSink connection) =>
        QuestWireMapping.Send(quest, (write, capacity) => SendSized(connection, write, capacity));

    private void SendEventsOnly(in WorldSnapshot snapshot, IPacketSink connection)
    {
        _clockTick = snapshot.ServerTick;
        _clockPending = true;
        SendEvents(snapshot.Events, connection);
        SendSkillEvents(snapshot.SkillEvents, connection);
        SendStatusEvents(snapshot.StatusEvents, snapshot, connection, withSpawned: false);
        SendItemEffects(snapshot.ItemEffects, connection);
        SendOwner(snapshot.Owner, connection);
        SendPremium(snapshot.Owner, connection);
        SendCooldowns(snapshot, connection);
    }

    // In a crowd denser than the visible cap, only the neighbourhood that holds the nearest MaxVisible entities (and the
    // keep band around it) is collected: a square of half size h holding at least that many puts all of them within
    // h times the square root of two.
    private static float CrowdRadius(EntityGrid grid, Vector3 center)
    {
        if (grid.Count <= MaxVisible * 2)
        {
            return ForgetRadius;
        }

        for (var half = EntityGrid.CellSize * 1.5f; half * 1.42f * 1.15f < ForgetRadius; half *= 1.6f)
        {
            if (grid.CountInSquare(center, half) >= MaxVisible * 2)
            {
                return half * 1.42f * 1.15f;
            }
        }

        return ForgetRadius;
    }

    private void ConsiderVisible(in WorldSnapshot snapshot, int field, int index, Vector3 center, bool essential)
    {
        ref readonly var view = ref snapshot.Entities[index];
        if (view.EntityId == _self || view.Hidden || HiddenInArena(field, view))
        {
            return;
        }

        var distanceSq = PlanarDistanceSq(view.Position, center);
        if (distanceSq > ForgetRadius * ForgetRadius || distanceSq > ViewRadius * ViewRadius && !_known.ContainsKey(view.EntityId))
        {
            return;
        }

        _visible.Add(index, distanceSq, essential);
    }

    // The duel arena is solo: a player sees only the opponent and helper of its own duel.
    private bool HiddenInArena(int field, in EntityView view) =>
        field == QuestArena.FieldCode && view.Kind != EntityKindView.Npc && (view.Kind == EntityKindView.Player || view.DuelPlayerId != _self);

    private void SendSized(IPacketSink connection, Func<Span<byte>, int> write, int capacity)
    {
        if (capacity > _scratch.Length)
        {
            throw new InvalidOperationException("Quest message exceeds the frame size.");
        }

        connection.Send(_scratch.AsSpan(0, write(_scratch)));
    }

    // Writes spawns and discrete changes immediately. Returns the age in ticks of a due position-only update, which the
    // caller schedules under the movement budget, or null when nothing else is due.
    private uint? WriteEntity(ref PresenceEntriesWriter group, IPacketSink connection, int index, uint tick, int field, bool near)
    {
        ref readonly var view = ref _entities[index];
        ref var known = ref CollectionsMarshal.GetValueRefOrAddDefault(_known, view.EntityId, out var exists);
        if (!exists)
        {
            var descriptor = view.Revision == 0 ? DescriptorHash(view) : 0;
            known = new Known(view, view.Revision == 0 ? (byte)1 : view.Revision, tick, descriptor, _pass);
            _spawned.Add(view.EntityId);
            AppendFull(ref group, connection, index, field, known.Revision);
            return null;
        }

        known.Pass = _pass;
        var discrete = known.MoveState != view.MoveState || known.Hp != view.Hp || known.AttackTargetId != view.AttackTargetId
            || known.AttackSequence != view.AttackSequence;
        if (view.Revision != 0)
        {
            if (view.Revision != known.Revision)
            {
                known.Revision = view.Revision;
                discrete = true;
            }
        }
        else
        {
            // A view built outside a field carries no revision; the encoder numbers its descriptions itself.
            var descriptor = DescriptorHash(view);
            if (descriptor != known.Descriptor)
            {
                known.Descriptor = descriptor;
                known.Revision = (byte)(known.Revision == byte.MaxValue ? 1 : known.Revision + 1);
                discrete = true;
            }
        }

        if (discrete)
        {
            known.Sent(view, tick);
            AppendFull(ref group, connection, index, field, known.Revision);
            return null;
        }

        var moved = known.Position != view.Position || known.Facing != view.Facing;
        var age = tick - known.LastSentTick;
        if (!moved || age < (near ? NearIntervalTicks : FarIntervalTicks))
        {
            return null;
        }

        if (view.EntityId == _self)
        {
            WriteMotion(ref group, connection, index, tick);
            return null;
        }

        return age;
    }

    private void WriteMotion(ref PresenceEntriesWriter group, IPacketSink connection, int index, uint tick)
    {
        ref readonly var view = ref _entities[index];
        ref var known = ref CollectionsMarshal.GetValueRefOrNullRef(_known, view.EntityId);
        known.Sent(view, tick);
        EnsureSpace(ref group, connection, PresenceWire.MaxMotionEntrySize);
        var entry = group.Reserve(PresenceWire.MaxMotionEntrySize);
        int size;
        if (_wire is { } wire && view.Revision != 0)
        {
            var shared = wire.Motion(index, view);
            shared.CopyTo(entry);
            size = shared.Length;
        }
        else
        {
            size = WireMapping.WriteMotion(entry, view);
        }

        group.Commit(size);
    }

    private void AppendFull(ref PresenceEntriesWriter group, IPacketSink connection, int index, int field, byte revision)
    {
        ref readonly var view = ref _entities[index];
        EnsureSpace(ref group, connection, PresenceWire.MaxFullEntrySize);
        var entry = group.Reserve(PresenceWire.MaxFullEntrySize);
        int size;
        if (_wire is { } wire && view.Revision != 0)
        {
            var shared = wire.Full(index, view, field);
            shared.CopyTo(entry);
            size = shared.Length;
        }
        else
        {
            size = WireMapping.WriteFull(entry, view, field, revision);
        }

        group.Commit(size);
    }

    private void EnsureSpace(ref PresenceEntriesWriter group, IPacketSink connection, int size)
    {
        if (group.Free < size)
        {
            FlushGroup(ref group, connection);
        }
    }

    private void FlushGroup(ref PresenceEntriesWriter group, IPacketSink connection)
    {
        if (group.Count == 0)
        {
            return;
        }

        SendClock(connection);
        connection.Send(group.Finish());
        group.Reset();
    }

    private void ForgetAll(IPacketSink connection)
    {
        _removed.Clear();
        _removed.AddRange(_known.Keys.Where(serial => serial != _self));
        _known.Clear();
        SendRemovals(connection);
        _goneDrops.Clear();
        _goneDrops.AddRange(_drops.Keys);
        _drops.Clear();
        SendDropRemovals(connection);
    }

    private void SendRemovals(IPacketSink connection)
    {
        for (var offset = 0; offset < _removed.Count; offset += RemoveObjectList.MaxSerials)
        {
            var count = Math.Min(RemoveObjectList.MaxSerials, _removed.Count - offset);
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_removed).Slice(offset, count);
            connection.Send(_scratch.AsSpan(0, RemoveObjectList.Write(_scratch, span)));
        }

        _removed.Clear();
    }

    private void SendEvents(ReadOnlySpan<CombatEventView> combat, IPacketSink connection)
    {
        _events.Clear();
        foreach (var combatEvent in combat)
        {
            if (combatEvent.AttackerId == _self || combatEvent.TargetId == _self
                || _known.ContainsKey(combatEvent.AttackerId) || _known.ContainsKey(combatEvent.TargetId))
            {
                _events.Add(WireMapping.Event(combatEvent));
            }
        }

        if (_events.Count > 0)
        {
            SendClock(connection);
        }

        var events = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_events);
        for (var offset = 0; offset < events.Length; offset += CombatEventList.MaxEvents)
        {
            var batch = events.Slice(offset, Math.Min(CombatEventList.MaxEvents, events.Length - offset));
            connection.Send(_scratch.AsSpan(0, CombatEventList.Write(_scratch, batch)));
        }
    }

    private bool Relevant(uint first, uint second) =>
        first == _self || second == _self || _known.ContainsKey(first) || second != 0 && _known.ContainsKey(second);

    private void SendSkillEvents(ReadOnlySpan<SkillEventView> skills, IPacketSink connection)
    {
        _skillEvents.Clear();
        foreach (var skillEvent in skills)
        {
            if (Relevant(skillEvent.CasterId, skillEvent.TargetId))
            {
                _skillEvents.Add(WireMapping.SkillEvent(skillEvent));
            }
        }

        if (_skillEvents.Count == 0)
        {
            return;
        }

        SendClock(connection);
        var events = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_skillEvents);
        for (var offset = 0; offset < events.Length; offset += SkillEventList.MaxEvents)
        {
            var batch = events.Slice(offset, Math.Min(SkillEventList.MaxEvents, events.Length - offset));
            connection.Send(_scratch.AsSpan(0, SkillEventList.Write(_scratch, batch)));
        }
    }

    private void SendStatusEvents(ReadOnlySpan<StatusEventView> statuses, in WorldSnapshot snapshot, IPacketSink connection, bool withSpawned)
    {
        _statusEvents.Clear();
        foreach (var status in statuses)
        {
            if (Relevant(status.EntityId, 0))
            {
                _statusEvents.Add(WireMapping.Status(status));
            }
        }

        foreach (var entity in withSpawned ? _spawned : [])
        {
            foreach (var running in snapshot.ActiveStatuses.For(entity))
            {
                if (running.RemainingAt(snapshot.FieldNowMs) > 0 && !PublishedWith(snapshot.StatusEvents, running.Status))
                {
                    _statusEvents.Add(WireMapping.Status(running.At(snapshot.FieldNowMs)));
                }
            }
        }

        if (_statusEvents.Count == 0)
        {
            return;
        }

        SendClock(connection);
        var entries = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_statusEvents);
        for (var offset = 0; offset < entries.Length; offset += StatusEffectList.MaxEntries)
        {
            var batch = entries.Slice(offset, Math.Min(StatusEffectList.MaxEntries, entries.Length - offset));
            connection.Send(_scratch.AsSpan(0, StatusEffectList.Write(_scratch, batch)));
        }
    }

    // A status that changed in the snapshot itself already reaches the receiver through the snapshot's own events.
    private static bool PublishedWith(StatusEventView[] events, in StatusEventView status)
    {
        foreach (var published in events)
        {
            if (published.EntityId == status.EntityId && published.StatusCode == status.StatusCode && published.SourceId == status.SourceId)
            {
                return true;
            }
        }

        return false;
    }

    private void SendCooldowns(in WorldSnapshot snapshot, IPacketSink connection)
    {
        if (snapshot.Cooldowns is not { } cooldowns)
        {
            return;
        }

        var count = Math.Min(cooldowns.Length, SkillCooldownsPacket.MaxEntries);
        var entries = new SkillCooldownEntry[count];
        for (var index = 0; index < count; index++)
        {
            var cooldown = cooldowns[index];
            entries[index] = new SkillCooldownEntry(cooldown.SkillCode, (uint)Math.Max(0, cooldown.RemainingMs), (uint)Math.Max(0, cooldown.DurationMs));
        }

        connection.Send(_scratch.AsSpan(0, new SkillCooldownsPacket(_clockTick, entries).Write(_scratch)));
    }

    private void SendDrops(in WorldSnapshot snapshot, Vector3 selfPosition, IPacketSink connection)
    {
        _visibleDrops.Clear();
        _newDrops.Clear();
        foreach (var drop in snapshot.GroundDrops)
        {
            if (!drop.Public && drop.OwnerId != _self || PlanarDistanceSq(drop.View.Position, selfPosition) > ItemRadius * ItemRadius)
            {
                continue;
            }

            _visibleDrops.Add(drop.View.Id);
            if (_drops.TryAdd(drop.View.Id, drop.View))
            {
                _newDrops.Add(WireMapping.Drop(drop.View));
            }
        }

        var added = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_newDrops);
        for (var offset = 0; offset < added.Length; offset += GroundItemList.MaxItems)
        {
            var batch = added.Slice(offset, Math.Min(GroundItemList.MaxItems, added.Length - offset));
            connection.Send(_scratch.AsSpan(0, GroundItemList.Write(_scratch, batch)));
        }

        _goneDrops.Clear();
        foreach (var id in _drops.Keys)
        {
            if (!_visibleDrops.Contains(id))
            {
                _goneDrops.Add(id);
            }
        }

        foreach (var id in _goneDrops)
        {
            _drops.Remove(id);
        }

        SendDropRemovals(connection);
    }

    private void SendDropRemovals(IPacketSink connection)
    {
        var gone = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_goneDrops);
        for (var offset = 0; offset < gone.Length; offset += GroundItemRemovals.MaxItems)
        {
            var batch = gone.Slice(offset, Math.Min(GroundItemRemovals.MaxItems, gone.Length - offset));
            connection.Send(_scratch.AsSpan(0, GroundItemRemovals.Write(_scratch, batch)));
        }

        _goneDrops.Clear();
    }

    private void SendOwner(OwnerState? owner, IPacketSink connection)
    {
        if (owner is null || ReferenceEquals(owner, _owner))
        {
            return;
        }

        _owner = owner;
        connection.Send(_scratch.AsSpan(0, ItemWireMapping.Status(owner).Write(_scratch)));
        connection.Send(_scratch.AsSpan(0, WireMapping.Status(owner).Write(_scratch)));
        if (owner.InventoryRevision != _inventoryRevision)
        {
            _inventoryRevision = owner.InventoryRevision;
            ItemWireMapping.SendDetails(owner, connection, _scratch);
            var items = owner.Items.Select(WireMapping.Item).ToArray();
            var parts = Math.Max(1, (items.Length + InventoryPartPacket.MaxItemsPerPart - 1) / InventoryPartPacket.MaxItemsPerPart);
            for (var part = 0; part < parts; part++)
            {
                var slice = items.Skip(part * InventoryPartPacket.MaxItemsPerPart).Take(InventoryPartPacket.MaxItemsPerPart).ToArray();
                var packet = new InventoryPartPacket(owner.InventoryRevision, part, parts, slice);
                connection.Send(_scratch.AsSpan(0, packet.Write(_scratch)));
            }
        }

        if (owner.SkillsRevision != _skillsRevision)
        {
            _skillsRevision = owner.SkillsRevision;
            var skills = owner.Skills.Take(SkillsPacket.MaxSkills).Select(skill => (skill.Code, skill.Rank)).ToArray();
            connection.Send(_scratch.AsSpan(0, new SkillsPacket(owner.SkillsRevision, skills).Write(_scratch)));
        }
    }

    // Precedes the first presence entry of each tick so the client can place samples and events on the server timeline.
    private void SendClock(IPacketSink connection)
    {
        if (!_clockPending)
        {
            return;
        }

        _clockPending = false;
        var length = new PresenceClockPacket(_clockTick, TickIntervalMs).Write(_scratch);
        connection.Send(_scratch.AsSpan(0, length));
    }

    private void SendCommand(IPacketSink connection, TransCommand command, bool encrypt)
    {
        var length = command.Write(_scratch);
        if (encrypt)
        {
            connection.SendEncrypted(_scratch.AsSpan(0, length));
        }
        else
        {
            connection.Send(_scratch.AsSpan(0, length));
        }
    }

    private static int DescriptorHash(in EntityView view)
    {
        var appearance = view.Appearance;
        return HashCode.Combine(view.MaxHp, view.Level, view.TemplateId, view.MoveSpeed, view.AttackRate,
            HashCode.Combine(appearance.WeaponKey, appearance.ArmorKey, appearance.OffHandKey, appearance.InVillage, appearance.Tier, appearance.HairIndex,
                HashCode.Combine(appearance.HelperPet, appearance.Phoenix, appearance.BigHead)), view.OwnerId,
            view.ModelScale);
    }

    private static float PlanarDistanceSq(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    // What this receiver last got about an entity, only the fields its diff compares (a crowd keeps one per pair); Pass
    // marks the entities seen in the snapshot being encoded.
    private struct Known
    {
        public Vector3 Position;
        public float Facing;
        public int Hp;
        public uint AttackTargetId;
        public ulong AttackSequence;
        public uint LastSentTick;
        public uint Pass;
        public int Descriptor;
        public MoveStateView MoveState;
        public EntityKindView Kind;
        public byte Revision;

        public Known(in EntityView view, byte revision, uint tick, int descriptor, uint pass)
        {
            Kind = view.Kind;
            Revision = revision;
            Descriptor = descriptor;
            Pass = pass;
            Sent(view, tick);
        }

        public void Sent(in EntityView view, uint tick)
        {
            Position = view.Position;
            Facing = view.Facing;
            Hp = view.Hp;
            AttackTargetId = view.AttackTargetId;
            AttackSequence = view.AttackSequence;
            MoveState = view.MoveState;
            LastSentTick = tick;
        }
    }
}
