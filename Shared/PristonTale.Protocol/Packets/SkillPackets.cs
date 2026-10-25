using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Etervale skill messages. Every body starts with {ushort Version, ushort Flags}; readers accept newer versions that
// append fields after the ones they know. Positions are play-data fixed point and angles are 4096 per turn.
// Flags (version 2) carry the command: 0 a new cast, 1 the release of a held charge, 2 and 3 order the caster's summon
// to fight only its owner's target or to pick its own (SkillCastFlags).
// {header, uint16 Version = 3, uint16 Flags, uint32 Sequence, SkillCode, uint64 TargetSerial, int32 X, Y, Z, int16 Angle,
// int16 0, uint32 ClientTimeMs}.
public readonly record struct SkillCastPacket(uint Sequence, uint SkillCode, ulong TargetSerial, int X, int Y, int Z, short Angle, uint ClientTimeMs,
    ushort Flags = 0)
{
    public const ushort CurrentVersion = 3;
    public const int Size = 8 + 4 + 4 * 2 + 8 + 4 * 3 + 4 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.SkillCast);
        writer.UInt16(CurrentVersion);
        writer.UInt16(Flags);
        writer.UInt32(Sequence);
        writer.UInt32(SkillCode);
        writer.UInt64(TargetSerial);
        writer.Int32(X);
        writer.Int32(Y);
        writer.Int32(Z);
        writer.Int16(Angle);
        writer.Zero(2);
        writer.UInt32(ClientTimeMs);
        return writer.Finish();
    }

    public static SkillCastPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        var version = reader.UInt16();
        var flags = reader.UInt16();
        if (version < CurrentVersion || flags > SkillCastFlags.SummonAggressive)
        {
            throw new InvalidDataException("Skill cast has an older layout or an unknown command.");
        }

        var sequence = reader.UInt32();
        var code = reader.UInt32();
        var target = reader.UInt64();
        var x = reader.Int32();
        var y = reader.Int32();
        var z = reader.Int32();
        var angle = reader.Int16();
        reader.Skip(2);
        return new SkillCastPacket(sequence, code, target, x, y, z, angle, reader.UInt32(), flags);
    }
}

public static class SkillCastFlags
{
    public const ushort Cast = 0;
    public const ushort Release = 1;
    public const ushort SummonPassive = 2;
    public const ushort SummonAggressive = 3;
}

public enum SkillCastStatus : byte
{
    Accepted = 0,
    NotLearned = 1,
    NotCastable = 2,
    Cooldown = 3,
    NotEnoughMana = 4,
    NotEnoughStamina = 5,
    InvalidTarget = 6,
    OutOfRange = 7,
    Busy = 8,
    Dead = 9,
    WeaponNotAllowed = 10,
    Stale = 11,
    InVillage = 12,
    Rejected = 13,
    AlreadyActive = 14
}

// Answer to the caster's own SkillCast, correlated by Sequence. CooldownMs is the full cooldown that started.
public readonly record struct SkillCastResultPacket(uint Sequence, uint SkillCode, SkillCastStatus Status, byte Rank, uint CooldownMs, uint RemainingMs,
    int Mana, int Stamina, uint ServerTick)
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4 + 4 * 2 + 4 + 4 * 5;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.SkillCastResult);
        writer.UInt16(CurrentVersion);
        writer.UInt16(0);
        writer.UInt32(Sequence);
        writer.UInt32(SkillCode);
        writer.Byte((byte)Status);
        writer.Byte(Rank);
        writer.Zero(2);
        writer.UInt32(CooldownMs);
        writer.UInt32(RemainingMs);
        writer.Int32(Mana);
        writer.Int32(Stamina);
        writer.UInt32(ServerTick);
        return writer.Finish();
    }

    public static SkillCastResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        SkillWire.ReadVersion(ref reader, "Skill cast result");
        var sequence = reader.UInt32();
        var code = reader.UInt32();
        var status = reader.Byte();
        var rank = reader.Byte();
        reader.Skip(2);
        if (!Enum.IsDefined((SkillCastStatus)status))
        {
            throw new InvalidDataException("Skill cast result has an unknown status.");
        }

        return new SkillCastResultPacket(sequence, code, (SkillCastStatus)status, rank, reader.UInt32(), reader.UInt32(), reader.Int32(), reader.Int32(),
            reader.UInt32());
    }
}

public enum SkillEventPhase : byte
{
    // The caster begins the skill motion; the impact follows HitAtMs after this event's server time.
    Start = 1,
    // The skill's effect resolves at the position; damage and heals follow as combat events.
    Impact = 2,
    // The cast ended before its impact (death, field change).
    Cancel = 3,
    // A held charge was released early: the motion jumps ahead; DurationMs and HitAtMs give the remaining timeline.
    Release = 4
}

public static class SkillEventFlags
{
    public const ushort Area = 1;
    public const ushort Projectile = 2;
    public const ushort SelfCast = 4;
    // A link of a chain: the impact runs from the previous link's target (Position) to Target.
    public const ushort Chain = 8;
    // A strike of the caster's familiar (a falcon, orbs, a spirit or phantom weapon) at Target.
    public const ushort Companion = 16;
    // A support effect reached an ally (Target) other than the caster.
    public const ushort Ally = 32;
    // A shield's counter strike, a fully charged resolution; two bits name the cast's mode.
    public const ushort Counter = 64;
    public const ushort Charged = 128;
    public const ushort ModeShift = 8;
    public const ushort ModeMask = 0x300;
}

// {uint64 Caster, Target, uint32 SkillCode, uint8 Phase, Rank, uint16 Flags, float32 DurationMs, HitAtMs, int32 X, Y, Z,
// int16 Angle, uint16 TargetCount, int32 Radius, int32 0}.
public readonly record struct SkillEventEntry(
    ulong Caster,
    ulong Target,
    uint SkillCode,
    SkillEventPhase Phase,
    byte Rank,
    ushort Flags,
    float DurationMs,
    float HitAtMs,
    int X,
    int Y,
    int Z,
    short Angle,
    ushort TargetCount,
    int Radius)
{
    public const int Size = 56;

    public void Write(ref PacketWriter writer)
    {
        writer.UInt64(Caster);
        writer.UInt64(Target);
        writer.UInt32(SkillCode);
        writer.Byte((byte)Phase);
        writer.Byte(Rank);
        writer.UInt16(Flags);
        writer.Single(DurationMs);
        writer.Single(HitAtMs);
        writer.Int32(X);
        writer.Int32(Y);
        writer.Int32(Z);
        writer.Int16(Angle);
        writer.UInt16(TargetCount);
        writer.Int32(Radius);
        writer.Zero(4);
    }

    public static SkillEventEntry Read(ref PacketReader reader)
    {
        var caster = reader.UInt64();
        var target = reader.UInt64();
        var code = reader.UInt32();
        var phase = reader.Byte();
        if (phase is < (byte)SkillEventPhase.Start or > (byte)SkillEventPhase.Release)
        {
            throw new InvalidDataException("Skill event has an unknown phase.");
        }

        var entry = new SkillEventEntry(caster, target, code, (SkillEventPhase)phase, reader.Byte(), reader.UInt16(), reader.Single(), reader.Single(),
            reader.Int32(), reader.Int32(), reader.Int32(), reader.Int16(), reader.UInt16(), reader.Int32());
        reader.Skip(4);
        if (!float.IsFinite(entry.DurationMs) || !float.IsFinite(entry.HitAtMs) || entry.DurationMs < 0f || entry.HitAtMs < 0f)
        {
            throw new InvalidDataException("Skill event has invalid timing.");
        }

        return entry;
    }
}

// {size, code, Version, Flags, Count, entries}: skill starts and impacts on the presence timeline of the last clock.
public static class SkillEventList
{
    public const ushort CurrentVersion = 3;
    public const int HeaderSize = 8 + 4 + 4;
    public const int MaxEvents = (PristonFrameCodec.MaxFrameSize - HeaderSize) / SkillEventEntry.Size;

    public static int Write(Span<byte> destination, ReadOnlySpan<SkillEventEntry> events)
    {
        if (events.Length > MaxEvents)
        {
            throw new ArgumentOutOfRangeException(nameof(events));
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.SkillEvents);
        writer.UInt16(CurrentVersion);
        writer.UInt16(0);
        writer.Int32(events.Length);
        foreach (var entry in events)
        {
            entry.Write(ref writer);
        }

        return writer.Finish();
    }

    public static SkillEventEntry[] Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        SkillWire.ReadVersion(ref reader, "Skill event list");
        var count = reader.Int32();
        if (count < 0 || count > MaxEvents || reader.Remaining != count * SkillEventEntry.Size)
        {
            throw new InvalidDataException("Skill event list has an invalid count.");
        }

        var events = new SkillEventEntry[count];
        for (var index = 0; index < count; index++)
        {
            events[index] = SkillEventEntry.Read(ref reader);
        }

        return events;
    }
}

public enum StatusEffectOp : byte
{
    Add = 1,
    Refresh = 2,
    Remove = 3,
    // A periodic effect applied its value (damage or restore) this tick.
    Tick = 4
}

public static class StatusEffectFlags
{
    public const ushort Harmful = 1;
    // No timer: the effect lasts while its source holds it (an equipped item, an aura).
    public const ushort Permanent = 2;
}

// One change of a modifier effect on an entity. The layout is source-agnostic: SourceKind tells items, player skills,
// monster skills, consumables and auras apart, and Control carries the control states (stun, root, silence, slow,
// hidden) the effect imposes. Value is the periodic amount for Tick entries and the main magnitude otherwise.
// {uint64 Entity, Source, uint32 StatusCode, uint8 Op, Stacks, SourceKind, Control, uint16 Flags, int16 0, int32 DurationMs,
// RemainingMs, Value}.
public readonly record struct StatusEffectEntry(
    ulong Entity,
    ulong Source,
    uint StatusCode,
    StatusEffectOp Op,
    byte Stacks,
    byte SourceKind,
    byte Control,
    ushort Flags,
    int DurationMs,
    int RemainingMs,
    int Value)
{
    public const int Size = 40;

    public void Write(ref PacketWriter writer)
    {
        writer.UInt64(Entity);
        writer.UInt64(Source);
        writer.UInt32(StatusCode);
        writer.Byte((byte)Op);
        writer.Byte(Stacks);
        writer.Byte(SourceKind);
        writer.Byte(Control);
        writer.UInt16(Flags);
        writer.Zero(2);
        writer.Int32(DurationMs);
        writer.Int32(RemainingMs);
        writer.Int32(Value);
    }

    public static StatusEffectEntry Read(ref PacketReader reader)
    {
        var entity = reader.UInt64();
        var source = reader.UInt64();
        var code = reader.UInt32();
        var op = reader.Byte();
        if (op is < (byte)StatusEffectOp.Add or > (byte)StatusEffectOp.Tick)
        {
            throw new InvalidDataException("Status effect has an unknown operation.");
        }

        var stacks = reader.Byte();
        var sourceKind = reader.Byte();
        var control = reader.Byte();
        var flags = reader.UInt16();
        reader.Skip(2);
        var entry = new StatusEffectEntry(entity, source, code, (StatusEffectOp)op, stacks, sourceKind, control, flags, reader.Int32(), reader.Int32(),
            reader.Int32());
        if (entry.DurationMs < 0 || entry.RemainingMs < 0 || entry.RemainingMs > entry.DurationMs)
        {
            throw new InvalidDataException("Status effect has invalid timers.");
        }

        return entry;
    }
}

// {size, code, Version, Flags, Count, entries}: status changes on the presence timeline of the last clock.
public static class StatusEffectList
{
    public const ushort CurrentVersion = 2;
    public const int HeaderSize = 8 + 4 + 4;
    public const int MaxEntries = (PristonFrameCodec.MaxFrameSize - HeaderSize) / StatusEffectEntry.Size;

    public static int Write(Span<byte> destination, ReadOnlySpan<StatusEffectEntry> entries)
    {
        if (entries.Length > MaxEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(entries));
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.StatusEffects);
        writer.UInt16(CurrentVersion);
        writer.UInt16(0);
        writer.Int32(entries.Length);
        foreach (var entry in entries)
        {
            entry.Write(ref writer);
        }

        return writer.Finish();
    }

    public static StatusEffectEntry[] Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        SkillWire.ReadVersion(ref reader, "Status effect list");
        var count = reader.Int32();
        if (count < 0 || count > MaxEntries || reader.Remaining != count * StatusEffectEntry.Size)
        {
            throw new InvalidDataException("Status effect list has an invalid count.");
        }

        var entries = new StatusEffectEntry[count];
        for (var index = 0; index < count; index++)
        {
            entries[index] = StatusEffectEntry.Read(ref reader);
        }

        return entries;
    }
}

public readonly record struct SkillCooldownEntry(uint SkillCode, uint RemainingMs, uint DurationMs)
{
    public const int Size = 12;
}

// The owner's running cooldowns at ServerTick: {size, code, Version, Flags, ServerTick, Count, entries}. A skill that is
// missing from the list is ready. Sent when the set changes, so a reconnecting or warping client resynchronizes.
public sealed record SkillCooldownsPacket(uint ServerTick, IReadOnlyList<SkillCooldownEntry> Cooldowns)
{
    public const ushort CurrentVersion = 1;
    public const int HeaderSize = 8 + 4 + 4 + 4;
    public const int MaxEntries = 256;

    public int Write(Span<byte> destination)
    {
        if (Cooldowns.Count > MaxEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(destination));
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.SkillCooldowns);
        writer.UInt16(CurrentVersion);
        writer.UInt16(0);
        writer.UInt32(ServerTick);
        writer.Int32(Cooldowns.Count);
        foreach (var entry in Cooldowns)
        {
            writer.UInt32(entry.SkillCode);
            writer.UInt32(entry.RemainingMs);
            writer.UInt32(entry.DurationMs);
        }

        return writer.Finish();
    }

    public static SkillCooldownsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        SkillWire.ReadVersion(ref reader, "Skill cooldowns");
        var tick = reader.UInt32();
        var count = reader.Int32();
        if (count < 0 || count > MaxEntries || reader.Remaining != count * SkillCooldownEntry.Size)
        {
            throw new InvalidDataException("Skill cooldown list has an invalid count.");
        }

        var entries = new SkillCooldownEntry[count];
        for (var index = 0; index < count; index++)
        {
            var entry = new SkillCooldownEntry(reader.UInt32(), reader.UInt32(), reader.UInt32());
            if (entry.RemainingMs > entry.DurationMs)
            {
                throw new InvalidDataException("Skill cooldown remains longer than its duration.");
            }

            entries[index] = entry;
        }

        return new SkillCooldownsPacket(tick, entries);
    }
}

internal static class SkillWire
{
    public static void ReadVersion(ref PacketReader reader, string name)
    {
        var version = reader.UInt16();
        reader.Skip(2);
        if (version == 0)
        {
            throw new InvalidDataException($"{name} has version 0.");
        }
    }
}
