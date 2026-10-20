using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Fixed little-endian structs for Etervale extension opcodes. They follow the original {size, code} header and the
// 8192-byte frame limit; lists that can exceed one frame are split into parts.
public sealed record OwnerStatusPacket(
    int Level,
    long Experience,
    long LevelStart,
    long NextLevel,
    long Money,
    int Hp,
    int MaxHp,
    int Mp,
    int MaxMp,
    int Sp,
    int MaxSp,
    int StatPoints,
    int SkillPoints,
    int Strength,
    int Spirit,
    int Talent,
    int Agility,
    int Health,
    int DamageMin,
    int DamageMax,
    int Defence,
    int Absorption,
    int AttackRating,
    int MoveSpeed,
    float AttackRange,
    long InventoryRevision,
    long ProgressionRevision,
    long SkillsRevision)
{
    public const int Size = 8 + 4 + 8 * 4 + 4 * 19 + 4 + 8 * 3;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.OwnerStatus);
        writer.Int32(Level);
        writer.Int64(Experience);
        writer.Int64(LevelStart);
        writer.Int64(NextLevel);
        writer.Int64(Money);
        writer.Int32(Hp);
        writer.Int32(MaxHp);
        writer.Int32(Mp);
        writer.Int32(MaxMp);
        writer.Int32(Sp);
        writer.Int32(MaxSp);
        writer.Int32(StatPoints);
        writer.Int32(SkillPoints);
        writer.Int32(Strength);
        writer.Int32(Spirit);
        writer.Int32(Talent);
        writer.Int32(Agility);
        writer.Int32(Health);
        writer.Int32(DamageMin);
        writer.Int32(DamageMax);
        writer.Int32(Defence);
        writer.Int32(Absorption);
        writer.Int32(AttackRating);
        writer.Int32(MoveSpeed);
        writer.Single(AttackRange);
        writer.Int64(InventoryRevision);
        writer.Int64(ProgressionRevision);
        writer.Int64(SkillsRevision);
        return writer.Finish();
    }

    public static OwnerStatusPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, Size);
        return new OwnerStatusPacket(r.Int32(), r.Int64(), r.Int64(), r.Int64(), r.Int64(), r.Int32(), r.Int32(), r.Int32(), r.Int32(),
            r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32(),
            r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Single(), r.Int64(), r.Int64(), r.Int64());
    }
}

public readonly record struct InventoryItemEntry(Guid InstanceId, uint Code, int Kind, int Count, int Anchor, int Slot, short Width, short Height, string Name)
{
    public const int Size = 16 + 4 * 5 + 2 * 2 + 32;
}

// {size, code, Revision, Part, Parts, Count, entries}. The client replaces its inventory when all parts of a revision arrive.
public sealed record InventoryPartPacket(long Revision, int Part, int Parts, IReadOnlyList<InventoryItemEntry> Items)
{
    public const int HeaderSize = 8 + 8 + 4 * 3;
    public const int MaxItemsPerPart = 100;

    public static int SizeFor(int count) => HeaderSize + count * InventoryItemEntry.Size;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.Inventory);
        writer.Int64(Revision);
        writer.Int32(Part);
        writer.Int32(Parts);
        writer.Int32(Items.Count);
        foreach (var item in Items)
        {
            writer.Guid(item.InstanceId);
            writer.UInt32(item.Code);
            writer.Int32(item.Kind);
            writer.Int32(item.Count);
            writer.Int32(item.Anchor);
            writer.Int32(item.Slot);
            writer.Int16(item.Width);
            writer.Int16(item.Height);
            writer.Text(item.Name, 32);
        }

        return writer.Finish();
    }

    public static InventoryPartPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        var revision = reader.Int64();
        var part = reader.Int32();
        var parts = reader.Int32();
        var count = reader.Int32();
        if (parts < 1 || part < 0 || part >= parts || count < 0 || count > MaxItemsPerPart || reader.Remaining != count * InventoryItemEntry.Size)
        {
            throw new InvalidDataException("Inventory part has invalid counts.");
        }

        var items = new InventoryItemEntry[count];
        for (var index = 0; index < count; index++)
        {
            items[index] = new InventoryItemEntry(reader.Guid(), reader.UInt32(), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(),
                reader.Int16(), reader.Int16(), reader.Text(32));
        }

        return new InventoryPartPacket(revision, part, parts, items);
    }
}

public sealed record SkillsPacket(long Revision, IReadOnlyList<(uint Code, int Rank)> Skills)
{
    public const int HeaderSize = 8 + 8 + 4;
    public const int MaxSkills = 512;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.Skills);
        writer.Int64(Revision);
        writer.Int32(Skills.Count);
        foreach (var (code, rank) in Skills)
        {
            writer.UInt32(code);
            writer.Int32(rank);
        }

        return writer.Finish();
    }

    public static SkillsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        var revision = reader.Int64();
        var count = reader.Int32();
        if (count < 0 || count > MaxSkills || reader.Remaining != count * 8)
        {
            throw new InvalidDataException("Skill list has an invalid count.");
        }

        var skills = new (uint, int)[count];
        for (var index = 0; index < count; index++)
        {
            skills[index] = (reader.UInt32(), reader.Int32());
        }

        return new SkillsPacket(revision, skills);
    }
}

public sealed record InventoryCommandPacket(Guid OperationId, long ExpectedRevision, int Action, Guid ItemId, int Destination, int Count, int Slot)
{
    public const int Size = 8 + 16 + 8 + 4 + 16 + 4 * 3;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.InventoryCommand);
        writer.Guid(OperationId);
        writer.Int64(ExpectedRevision);
        writer.Int32(Action);
        writer.Guid(ItemId);
        writer.Int32(Destination);
        writer.Int32(Count);
        writer.Int32(Slot);
        return writer.Finish();
    }

    public static InventoryCommandPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, Size);
        return new InventoryCommandPacket(r.Guid(), r.Int64(), r.Int32(), r.Guid(), r.Int32(), r.Int32(), r.Int32());
    }
}

public sealed record ProgressionCommandPacket(Guid OperationId, long ExpectedRevision, int Action, int Attribute, int Amount, uint SkillCode, uint NpcSerial)
{
    public const int Size = 8 + 16 + 8 + 4 * 5;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ProgressionCommand);
        writer.Guid(OperationId);
        writer.Int64(ExpectedRevision);
        writer.Int32(Action);
        writer.Int32(Attribute);
        writer.Int32(Amount);
        writer.UInt32(SkillCode);
        writer.UInt32(NpcSerial);
        return writer.Finish();
    }

    public static ProgressionCommandPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, Size);
        return new ProgressionCommandPacket(r.Guid(), r.Int64(), r.Int32(), r.Int32(), r.Int32(), r.UInt32(), r.UInt32());
    }
}

// Shared reply for inventory and progression commands, correlated by OperationId.
public sealed record OperationResultPacket(uint Code, Guid OperationId, bool Ok, long Revision, string Message)
{
    public const int Size = 8 + 16 + 4 + 8 + 64;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, Code);
        writer.Guid(OperationId);
        writer.Int32(Ok ? 1 : 0);
        writer.Int64(Revision);
        writer.Text(Message, 64);
        return writer.Finish();
    }

    public static OperationResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, Size);
        return new OperationResultPacket(r.Code, r.Guid(), r.Int32() != 0, r.Int64(), r.Text(64));
    }
}

// Completes an NPC click. Dialogue lines arrive first as WHISPERMESSAGE packets from the same NPC serial.
public sealed record NpcResultPacket(uint NpcSerial, bool Ok, uint Services, string Name, string Message)
{
    public const int Size = 8 + 4 * 3 + 32 + 64;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.NpcResult);
        writer.UInt32(NpcSerial);
        writer.Int32(Ok ? 1 : 0);
        writer.UInt32(Services);
        writer.Text(Name, 32);
        writer.Text(Message, 64);
        return writer.Finish();
    }

    public static NpcResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, Size);
        return new NpcResultPacket(r.UInt32(), r.Int32() != 0, r.UInt32(), r.Text(32), r.Text(64));
    }
}

public static class CombatEventFlags
{
    public const byte Critical = 1;
    public const byte Killed = 2;
}

public readonly record struct CombatEventEntry(
    uint Attacker,
    uint Target,
    byte Kind,
    byte Flags,
    float DurationMs,
    float HitAtMs,
    int Damage,
    int Resources,
    int SoundCode,
    int X,
    int Y,
    int Z,
    short Angle)
{
    public const int Size = 48;

    public void Write(ref PacketWriter writer)
    {
        writer.UInt32(Attacker);
        writer.UInt32(Target);
        writer.Byte(Kind);
        writer.Byte(Flags);
        writer.Zero(2);
        writer.Single(DurationMs);
        writer.Single(HitAtMs);
        writer.Int32(Damage);
        writer.Int32(Resources);
        writer.Int32(SoundCode);
        writer.Int32(X);
        writer.Int32(Y);
        writer.Int32(Z);
        writer.Int16(Angle);
        writer.Zero(2);
    }

    public static CombatEventEntry Read(ref PacketReader reader)
    {
        var attacker = reader.UInt32();
        var target = reader.UInt32();
        var kind = reader.Byte();
        var flags = reader.Byte();
        reader.Skip(2);
        var entry = new CombatEventEntry(attacker, target, kind, flags, reader.Single(), reader.Single(), reader.Int32(), reader.Int32(),
            reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int16());
        reader.Skip(2);
        return entry;
    }
}

// Resolved combat events ({size, code, Count, entries}); the original instead lets each client roll its own hits.
public static class CombatEventList
{
    public const int HeaderSize = 12;
    public const int MaxEvents = (PristonFrameCodec.MaxFrameSize - HeaderSize) / CombatEventEntry.Size;

    public static int SizeFor(int count) => HeaderSize + count * CombatEventEntry.Size;

    public static int Write(Span<byte> destination, ReadOnlySpan<CombatEventEntry> events)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CombatEvents);
        writer.Int32(events.Length);
        foreach (var entry in events)
        {
            entry.Write(ref writer);
        }

        return writer.Finish();
    }

    public static CombatEventEntry[] Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        var count = reader.Int32();
        if (count < 0 || count > MaxEvents || reader.Remaining != count * CombatEventEntry.Size)
        {
            throw new InvalidDataException("Combat event list has an invalid count.");
        }

        var events = new CombatEventEntry[count];
        for (var index = 0; index < count; index++)
        {
            events[index] = CombatEventEntry.Read(ref reader);
        }

        return events;
    }
}

// Objects leaving the receiver's interest area; the original client instead times them out after 4-30 seconds.
public static class RemoveObjectList
{
    public const int HeaderSize = 12;
    public const int MaxSerials = (PristonFrameCodec.MaxFrameSize - HeaderSize) / 4;

    public static int SizeFor(int count) => HeaderSize + count * 4;

    public static int Write(Span<byte> destination, ReadOnlySpan<uint> serials)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.RemoveObjects);
        writer.Int32(serials.Length);
        foreach (var serial in serials)
        {
            writer.UInt32(serial);
        }

        return writer.Finish();
    }

    public static uint[] Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        var count = reader.Int32();
        if (count < 0 || count > MaxSerials || reader.Remaining != count * 4)
        {
            throw new InvalidDataException("Object removal list has an invalid count.");
        }

        var serials = new uint[count];
        for (var index = 0; index < count; index++)
        {
            serials[index] = reader.UInt32();
        }

        return serials;
    }
}

// Server simulation clock for the presence entries that follow it on the connection: PLAYDATAGROUP samples and
// combat events until the next clock. Layout: {size, code, ushort Version, ushort Flags, uint ServerTick,
// uint TickIntervalMs}. Readers accept newer versions that append fields.
public readonly record struct PresenceClockPacket(uint ServerTick, uint TickIntervalMs)
{
    public const ushort CurrentVersion = 1;
    public const int Size = 20;

    public double ServerTimeMs => (double)ServerTick * TickIntervalMs;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PresenceClock);
        writer.UInt16(CurrentVersion);
        writer.UInt16(0);
        writer.UInt32(ServerTick);
        writer.UInt32(TickIntervalMs);
        return writer.Finish();
    }

    public static PresenceClockPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        var version = reader.UInt16();
        reader.Skip(2);
        if (version == 0)
        {
            throw new InvalidDataException("Presence clock has version 0.");
        }

        var tick = reader.UInt32();
        var interval = reader.UInt32();
        if (interval == 0)
        {
            throw new InvalidDataException("Presence clock has a zero tick interval.");
        }

        return new PresenceClockPacket(tick, interval);
    }
}
