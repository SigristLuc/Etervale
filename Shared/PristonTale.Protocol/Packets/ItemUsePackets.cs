using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Usable-item messages are Etervale extensions. Every body starts with {uint16 Version, uint16 Flags}; readers reject
// version 0 and accept newer versions that append fields, except the lists, whose length is checked exactly.
public static class ItemUseWire
{
    public const ushort Version = 1;
    public const int MaxTextBytes = 64;
    public const int MaxMessageBytes = 128;

    internal static void Header(ref PacketWriter writer)
    {
        writer.UInt16(Version);
        writer.UInt16(0);
    }

    internal static void ReadHeader(ref PacketReader reader)
    {
        if (reader.UInt16() == 0)
        {
            throw new InvalidDataException("Item use message has version 0.");
        }

        reader.Skip(2);
    }
}

// C->S: use one owned item. {header, Guid OperationId, int64 ExpectedRevision, Guid ItemId, int32 Argument, text Text}.
// Argument names a destination of a travel item; Text is the line a firecracker shows.
public sealed record ItemUseCommandPacket(Guid OperationId, long ExpectedRevision, Guid ItemId, int Argument, string Text)
{
    public const int MinimumSize = 8 + 4 + 16 + 8 + 16 + 4 + 2;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ItemUseCommand);
        ItemUseWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.Int64(ExpectedRevision);
        writer.Guid(ItemId);
        writer.Int32(Argument);
        NpcWire.Text(ref writer, Text, ItemUseWire.MaxTextBytes);
        return writer.Finish();
    }

    public static ItemUseCommandPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        ItemUseWire.ReadHeader(ref reader);
        return new ItemUseCommandPacket(reader.Guid(), reader.Int64(), reader.Guid(), reader.Int32(), NpcWire.Text(ref reader, ItemUseWire.MaxTextBytes));
    }
}

// S->C: the answer to one item use. {header, Guid OperationId, int32 Ok, int64 Revision, uint16 Status, uint16 0,
// uint32 ItemCode, text Message}.
public sealed record ItemUseResultPacket(Guid OperationId, bool Ok, long Revision, ushort Status, uint ItemCode, string Message)
{
    public const int MinimumSize = 8 + 4 + 16 + 4 + 8 + 4 + 4 + 2;
    public const int MaximumSize = MinimumSize + ItemUseWire.MaxMessageBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ItemUseResult);
        ItemUseWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.Int32(Ok ? 1 : 0);
        writer.Int64(Revision);
        writer.UInt16(Status);
        writer.UInt16(0);
        writer.UInt32(ItemCode);
        NpcWire.Text(ref writer, Message, ItemUseWire.MaxMessageBytes);
        return writer.Finish();
    }

    public static ItemUseResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        ItemUseWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var ok = reader.Int32() != 0;
        var revision = reader.Int64();
        var status = reader.UInt16();
        reader.Skip(2);
        var code = reader.UInt32();
        return new ItemUseResultPacket(operation, ok, revision, status, code, NpcWire.Text(ref reader, ItemUseWire.MaxMessageBytes));
    }
}

public readonly record struct PremiumTimerEntry(byte Kind, int Value, uint RemainingSeconds, uint DurationSeconds)
{
    public const int Size = 16;
}

// S->C, owner only: every running timed item effect. {header, int64 Revision, int32 Count, entries {uint8 Kind,
// uint8 0, uint16 0, int32 Value, uint32 RemainingSeconds, uint32 DurationSeconds}}.
public sealed record PremiumTimersPacket(long Revision, IReadOnlyList<PremiumTimerEntry> Timers)
{
    public const int HeaderSize = 8 + 4 + 8 + 4;
    public const int MaxTimers = 32;

    public int Write(Span<byte> destination)
    {
        if (Timers.Count > MaxTimers)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many timed item effects.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.PremiumTimers);
        ItemUseWire.Header(ref writer);
        writer.Int64(Revision);
        writer.Int32(Timers.Count);
        foreach (var timer in Timers)
        {
            writer.Byte(timer.Kind);
            writer.Zero(3);
            writer.Int32(timer.Value);
            writer.UInt32(timer.RemainingSeconds);
            writer.UInt32(timer.DurationSeconds);
        }

        return writer.Finish();
    }

    public static PremiumTimersPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        ItemUseWire.ReadHeader(ref reader);
        var revision = reader.Int64();
        var count = reader.Int32();
        if (count < 0 || count > MaxTimers || reader.Remaining != count * PremiumTimerEntry.Size)
        {
            throw new InvalidDataException("Timed item effect list has an invalid count.");
        }

        var timers = new PremiumTimerEntry[count];
        for (var index = 0; index < count; index++)
        {
            var kind = reader.Byte();
            reader.Skip(3);
            timers[index] = new PremiumTimerEntry(kind, reader.Int32(), reader.UInt32(), reader.UInt32());
        }

        return new PremiumTimersPacket(revision, timers);
    }
}

public readonly record struct ItemEffectEntry(ulong Entity, uint ItemCode, byte Effect, int X, int Y, int Z, int Variant)
{
    public const int Size = 32;
}

// S->C, presence timeline: items whose use plays a visible effect around a player (firecrackers, mastery). {header,
// int32 Count, entries {uint64 Entity, uint32 ItemCode, uint8 Effect, uint8 0, uint16 0, int32 X, Y, Z (x256),
// int32 Variant}}.
public sealed record ItemEffectsPacket(IReadOnlyList<ItemEffectEntry> Effects)
{
    public const int HeaderSize = 8 + 4 + 4;
    public const int MaxEffects = 64;

    public int Write(Span<byte> destination)
    {
        if (Effects.Count > MaxEffects)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many item effects.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.ItemEffects);
        ItemUseWire.Header(ref writer);
        writer.Int32(Effects.Count);
        foreach (var effect in Effects)
        {
            writer.UInt64(effect.Entity);
            writer.UInt32(effect.ItemCode);
            writer.Byte(effect.Effect);
            writer.Zero(3);
            writer.Int32(effect.X);
            writer.Int32(effect.Y);
            writer.Int32(effect.Z);
            writer.Int32(effect.Variant);
        }

        return writer.Finish();
    }

    public static ItemEffectsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        ItemUseWire.ReadHeader(ref reader);
        var count = reader.Int32();
        if (count < 0 || count > MaxEffects || reader.Remaining != count * ItemEffectEntry.Size)
        {
            throw new InvalidDataException("Item effect list has an invalid count.");
        }

        var effects = new ItemEffectEntry[count];
        for (var index = 0; index < count; index++)
        {
            var entity = reader.UInt64();
            var code = reader.UInt32();
            var effect = reader.Byte();
            reader.Skip(3);
            effects[index] = new ItemEffectEntry(entity, code, effect, reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());
        }

        return new ItemEffectsPacket(effects);
    }
}
