using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Server.Net;

// Turns one subscriber's share of a field snapshot into original Priston packets. It keeps the original's view
// ranges and distance-weighted detail, but queries the field grid instead of scanning
// every slot, sends an entity only when it changed, and lets the near ring update faster than the far ring.
internal sealed class PresenceEncoder
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
    private readonly Dictionary<uint, GroundDropView> _drops = new();
    private readonly List<int> _candidates = new();
    private readonly List<(int Index, float Priority, bool Near)> _pending = new();
    private readonly HashSet<uint> _seen = new();
    private readonly HashSet<uint> _visibleDrops = new();
    private readonly List<uint> _removed = new();
    private readonly List<CombatEventEntry> _events = new();
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

    public PresenceEncoder(uint self, WorldDataHolder data)
    {
        _self = self;
        _data = data;
    }

    public EntityKindView? KindOf(uint serial)
    {
        lock (_gate)
        {
            return _known.TryGetValue(serial, out var known) ? known.View.Kind : null;
        }
    }

    public Vector3? SelfPosition
    {
        get
        {
            lock (_gate)
            {
                return _known.TryGetValue(_self, out var known) ? known.View.Position : null;
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

    // PLAYERINFO answer for GETPLAYERINFO; null when the serial is not in this subscriber's view.
    public PlayerInfoPacket? Describe(uint serial)
    {
        EntityView view;
        lock (_gate)
        {
            if (!_known.TryGetValue(serial, out var known))
            {
                return null;
            }

            view = known.View;
        }

        var character = WireMapping.Describe(view, _data.Current);
        character.UpdateInfo[0] = Revision(serial);
        return new PlayerInfoPacket(character, view.EntityId, PristonUnits.ToFixed(view.Position.X), PristonUnits.ToFixed(view.Position.Y),
            PristonUnits.ToFixed(view.Position.Z), 0, PristonUnits.ToAngle(view.Facing), 0, WireMapping.ActionCode(view.MoveState));
    }

    private byte Revision(uint serial)
    {
        lock (_gate)
        {
            return _known.TryGetValue(serial, out var known) ? known.Revision : (byte)0;
        }
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
            return;
        }

        var self = snapshot.Entities[selfIndex];
        var field = snapshot.FieldId ?? 0;
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
        _clockTick = tick;
        _clockPending = true;
        var group = new PlayDataGroupWriter(_group, ++_groupCounter);
        _seen.Clear();
        _removed.Clear();
        WriteEntity(ref group, connection, self, tick, field, near: true);
        _seen.Add(_self);

        _candidates.Clear();
        _pending.Clear();
        grid.Query(self.Position, ForgetRadius, _candidates);
        foreach (var index in _candidates)
        {
            var view = snapshot.Entities[index];
            if (view.EntityId == _self)
            {
                continue;
            }

            var distanceSq = PlanarDistanceSq(view.Position, self.Position);
            var known = _known.ContainsKey(view.EntityId);
            if (distanceSq > ViewRadius * ViewRadius && !known || distanceSq > ForgetRadius * ForgetRadius)
            {
                continue;
            }

            _seen.Add(view.EntityId);
            var near = distanceSq <= NearRadius * NearRadius;
            if (WriteEntity(ref group, connection, view, tick, field, near) is { } age)
            {
                // Overdue ratio: the wanted interval grows with distance, and every waiting entity keeps gaining
                // priority, so a crowd is served round-robin like the original's LastTransAutoPlayCount cursor.
                var wanted = NearIntervalTicks + MathF.Sqrt(distanceSq) / 50f;
                _pending.Add((index, age / wanted, near));
            }
        }

        _pending.Sort(static (left, right) => right.Priority.CompareTo(left.Priority));
        var budget = MovementBudgetBytes;
        foreach (var (index, _, near) in _pending)
        {
            var size = near ? TransPlayData.SizeFor(1) : PlayDataCoarse.Size;
            if (budget < size)
            {
                break;
            }

            budget -= size;
            WriteMotion(ref group, connection, snapshot.Entities[index], tick, field, near);
        }

        foreach (var serial in _known.Keys)
        {
            if (!_seen.Contains(serial))
            {
                _removed.Add(serial);
            }
        }

        foreach (var serial in _removed)
        {
            _known.Remove(serial);
        }

        FlushGroup(ref group, connection);
        SendRemovals(connection);
        SendEvents(snapshot, connection);
        SendDrops(snapshot, self.Position, connection);
        SendOwner(snapshot.Owner, connection);
    }

    // Writes spawns and discrete changes immediately. Returns the age in ticks of a due position-only update, which the
    // caller schedules under the movement budget, or null when nothing else is due.
    private uint? WriteEntity(ref PlayDataGroupWriter group, IPacketSink connection, in EntityView view, uint tick, int field, bool near)
    {
        var descriptor = DescriptorHash(view);
        if (!_known.TryGetValue(view.EntityId, out var known))
        {
            known = new Known(view, 1, tick, descriptor);
            _known[view.EntityId] = known;
            AppendDetailed(ref group, connection, view, field, known.Revision);
            return null;
        }

        var previous = known.View;
        var discrete = previous.MoveState != view.MoveState || previous.Hp != view.Hp || previous.AttackTargetId != view.AttackTargetId
            || previous.AttackSequence != view.AttackSequence;
        var revision = known.Revision;
        if (descriptor != known.Descriptor)
        {
            revision = (byte)(revision == byte.MaxValue ? 1 : revision + 1);
            discrete = true;
        }

        if (discrete)
        {
            _known[view.EntityId] = new Known(view, revision, tick, descriptor);
            AppendDetailed(ref group, connection, view, field, revision);
            return null;
        }

        var moved = previous.Position != view.Position || previous.Facing != view.Facing;
        var age = tick - known.LastSentTick;
        if (!moved || age < (near ? NearIntervalTicks : FarIntervalTicks))
        {
            return null;
        }

        if (view.EntityId == _self)
        {
            WriteMotion(ref group, connection, view, tick, field, near: true);
            return null;
        }

        return age;
    }

    private void WriteMotion(ref PlayDataGroupWriter group, IPacketSink connection, in EntityView view, uint tick, int field, bool near)
    {
        var known = _known[view.EntityId];
        _known[view.EntityId] = known with { View = view, LastSentTick = tick };
        if (near)
        {
            AppendDetailed(ref group, connection, view, field, known.Revision);
            return;
        }

        var size = PlayDataCoarse.Size;
        EnsureSpace(ref group, connection, size);
        group.Commit(WireMapping.Coarse(view).Write(group.Reserve(size)));
    }

    private void AppendDetailed(ref PlayDataGroupWriter group, IPacketSink connection, in EntityView view, int field, byte revision)
    {
        var size = TransPlayData.SizeFor(1);
        EnsureSpace(ref group, connection, size);
        group.Commit(WireMapping.PlayData(view, field, revision).Write(group.Reserve(size)));
    }

    private void EnsureSpace(ref PlayDataGroupWriter group, IPacketSink connection, int size)
    {
        if (group.Free < size)
        {
            FlushGroup(ref group, connection);
        }
    }

    private void FlushGroup(ref PlayDataGroupWriter group, IPacketSink connection)
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
        foreach (var drop in _drops.Values)
        {
            SendDropRemoval(connection, drop);
        }

        _drops.Clear();
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

    private void SendEvents(in WorldSnapshot snapshot, IPacketSink connection)
    {
        _events.Clear();
        foreach (var combatEvent in snapshot.Events)
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

            _visibleDrops.Add(drop.View.Handle);
            if (_drops.TryAdd(drop.View.Handle, drop.View))
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

        _removed.Clear();
        foreach (var handle in _drops.Keys)
        {
            if (!_visibleDrops.Contains(handle))
            {
                _removed.Add(handle);
            }
        }

        foreach (var handle in _removed)
        {
            if (_drops.Remove(handle, out var drop))
            {
                SendDropRemoval(connection, drop);
            }
        }

        _removed.Clear();
    }

    private void SendDropRemoval(IPacketSink connection, GroundDropView drop)
    {
        var action = new GroundItemAction(PristonOpcodes.DelItem, 1, PristonUnits.ToFixed(drop.Position.X), PristonUnits.ToFixed(drop.Position.Y),
            PristonUnits.ToFixed(drop.Position.Z), drop.Handle);
        connection.Send(_scratch.AsSpan(0, action.Write(_scratch)));
    }

    private void SendOwner(OwnerState? owner, IPacketSink connection)
    {
        if (owner is null || ReferenceEquals(owner, _owner))
        {
            return;
        }

        _owner = owner;
        connection.Send(_scratch.AsSpan(0, WireMapping.Status(owner).Write(_scratch)));
        if (owner.InventoryRevision != _inventoryRevision)
        {
            _inventoryRevision = owner.InventoryRevision;
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
            HashCode.Combine(appearance.WeaponKey, appearance.ArmorKey, appearance.OffHandKey, appearance.InVillage, appearance.Tier));
    }

    private static float PlanarDistanceSq(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    private readonly record struct Known(EntityView View, byte Revision, uint LastSentTick, int Descriptor);
}
