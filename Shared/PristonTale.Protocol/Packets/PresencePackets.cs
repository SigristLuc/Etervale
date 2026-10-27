using System.Buffers.Binary;
using System.Numerics;
using PristonTale.Contracts.Motion;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Little-endian base-128 integers: 7 bits per byte, low group first, high bit set on every byte but the last.
public static class VarInt
{
    public const int MaxBytes64 = 10;

    public static int Size(ulong value)
    {
        var size = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            size++;
        }

        return size;
    }

    public static int Write(Span<byte> destination, ulong value)
    {
        var index = 0;
        while (value >= 0x80)
        {
            destination[index++] = (byte)(value | 0x80);
            value >>= 7;
        }

        destination[index++] = (byte)value;
        return index;
    }

    public static ulong Read(ReadOnlySpan<byte> source, ref int offset)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (offset >= source.Length)
            {
                throw new InvalidDataException("Variable-length integer runs past the packet.");
            }

            var part = source[offset++];
            // The tenth byte holds only bit 63, and a last byte of zero after the first would be an overlong encoding.
            if (shift == 63 && part > 1 || part == 0 && shift > 0)
            {
                throw new InvalidDataException("Variable-length integer is out of range or not minimal.");
            }

            value |= (ulong)(part & 0x7F) << shift;
            if ((part & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("Variable-length integer is longer than 64 bits.");
    }

    public static uint ZigZag(int value) => (uint)((value << 1) ^ (value >> 31));

    public static int UnZigZag(ulong value) => (int)(uint)(value >> 1) ^ -(int)(uint)(value & 1);
}

// What the receiver learns about one entity. A motion entry carries the position, facing and motion only; a full entry
// adds the life, level, target, attack sequence, description revision, shared template and field.
public readonly record struct PresenceEntry(
    ulong Serial,
    bool Full,
    Vector3 Position,
    float Facing,
    MotionState Motion,
    int Hp = 0,
    int MaxHp = 0,
    int Level = 0,
    ulong Target = 0,
    ulong AttackSequence = 0,
    byte Revision = 0,
    uint Template = 0,
    int FieldCode = -1);

// The presence of nearby entities (S->C). {header, uint16 Version = 1, uint16 Flags = 0, uint32 Sequence, uint16 Count,
// uint16 0}, then Count entries: {varuint Serial, uint8 Kind (bit 0 full), int24 X, Y, Z (world units x 8), uint16 Facing
// (65536 per turn), uint8 Motion} and, when full, {varuint Hp, MaxHp, Level, Target, AttackSequence, uint8 Revision,
// varuint Template, varint (zigzag) FieldCode}. Every integer that can grow is 64-bit capable; positions span the
// world's +-1,000,000 units at 1/8 of a unit.
public static class PresenceWire
{
    public const ushort Version = 1;
    public const int HeaderSize = 8 + 4 + 4 + 4;
    public const float PositionScale = 8f;
    private const int Int24Max = (1 << 23) - 1;
    private const byte FullKind = 1;
    private const int MotionBody = 1 + 9 + 2 + 1;

    // The largest entries, for buffer sizing.
    public const int MaxMotionEntrySize = VarInt.MaxBytes64 + MotionBody;
    public const int MaxFullEntrySize = MaxMotionEntrySize + 5 * 3 + VarInt.MaxBytes64 * 2 + 1 + 5 + 5;

    public static int MotionEntrySize(ulong serial) => VarInt.Size(serial) + MotionBody;

    public static int WriteMotion(Span<byte> destination, ulong serial, Vector3 position, float facing, MotionState motion)
    {
        var offset = VarInt.Write(destination, serial);
        destination[offset++] = 0;
        return WriteBody(destination, offset, position, facing, motion);
    }

    public static int WriteFull(Span<byte> destination, in PresenceEntry entry)
    {
        var offset = VarInt.Write(destination, entry.Serial);
        destination[offset++] = FullKind;
        offset = WriteBody(destination, offset, entry.Position, entry.Facing, entry.Motion);
        offset += VarInt.Write(destination[offset..], (ulong)Math.Max(0, entry.Hp));
        offset += VarInt.Write(destination[offset..], (ulong)Math.Max(0, entry.MaxHp));
        offset += VarInt.Write(destination[offset..], (ulong)Math.Max(0, entry.Level));
        offset += VarInt.Write(destination[offset..], entry.Target);
        offset += VarInt.Write(destination[offset..], entry.AttackSequence);
        destination[offset++] = entry.Revision;
        offset += VarInt.Write(destination[offset..], entry.Template);
        offset += VarInt.Write(destination[offset..], VarInt.ZigZag(entry.FieldCode));
        return offset;
    }

    public static PresenceEntry Read(ReadOnlySpan<byte> source, ref int offset)
    {
        var serial = VarInt.Read(source, ref offset);
        if (source.Length - offset < MotionBody)
        {
            throw new InvalidDataException("Presence entry ends inside its motion.");
        }

        var full = (source[offset++] & FullKind) != 0;
        var position = new Vector3(ReadInt24(source, ref offset), ReadInt24(source, ref offset), ReadInt24(source, ref offset)) / PositionScale;
        var facing = BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]) * (2f * MathF.PI / 65536f);
        offset += 2;
        var motion = (MotionState)source[offset++];
        if (motion > MotionState.Yahoo)
        {
            throw new InvalidDataException("Presence entry has an unknown motion.");
        }

        if (!full)
        {
            return new PresenceEntry(serial, false, position, facing, motion);
        }

        var hp = Narrow(VarInt.Read(source, ref offset));
        var maxHp = Narrow(VarInt.Read(source, ref offset));
        var level = Narrow(VarInt.Read(source, ref offset));
        var target = VarInt.Read(source, ref offset);
        var sequence = VarInt.Read(source, ref offset);
        if (offset >= source.Length)
        {
            throw new InvalidDataException("Presence entry ends before its revision.");
        }

        var revision = source[offset++];
        var template = VarInt.Read(source, ref offset) is var code && code <= uint.MaxValue ? (uint)code : throw new InvalidDataException("Presence template exceeds 32 bits.");
        var field = VarInt.UnZigZag(VarInt.Read(source, ref offset));
        return new PresenceEntry(serial, true, position, facing, motion, hp, maxHp, level, target, sequence, revision, template, field);
    }

    public static ushort ToFacing(float radians)
    {
        var turn = radians / (2f * MathF.PI);
        return (ushort)((int)MathF.Round((turn - MathF.Floor(turn)) * 65536f) & 0xFFFF);
    }

    private static int WriteBody(Span<byte> destination, int offset, Vector3 position, float facing, MotionState motion)
    {
        WriteInt24(destination, ref offset, position.X);
        WriteInt24(destination, ref offset, position.Y);
        WriteInt24(destination, ref offset, position.Z);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], ToFacing(facing));
        offset += 2;
        destination[offset++] = (byte)motion;
        return offset;
    }

    private static void WriteInt24(Span<byte> destination, ref int offset, float value)
    {
        var scaled = Math.Clamp((int)MathF.Round(value * PositionScale), -Int24Max, Int24Max);
        destination[offset] = (byte)scaled;
        destination[offset + 1] = (byte)(scaled >> 8);
        destination[offset + 2] = (byte)(scaled >> 16);
        offset += 3;
    }

    private static int ReadInt24(ReadOnlySpan<byte> source, ref int offset)
    {
        var value = source[offset] | source[offset + 1] << 8 | (sbyte)source[offset + 2] << 16;
        offset += 3;
        return value;
    }

    private static int Narrow(ulong value) => value > int.MaxValue ? throw new InvalidDataException("Presence value exceeds 31 bits.") : (int)value;
}

// Builds one presence message from entries; Finish writes the header.
public ref struct PresenceEntriesWriter
{
    private readonly Span<byte> _buffer;
    private readonly uint _sequence;
    private int _length;
    private int _count;

    public PresenceEntriesWriter(Span<byte> buffer, uint sequence)
    {
        _buffer = buffer;
        _sequence = sequence;
        _length = PresenceWire.HeaderSize;
        _count = 0;
    }

    public readonly int Count => _count;

    public readonly int Free => _buffer.Length - _length;

    public Span<byte> Reserve(int size) => _buffer.Slice(_length, size);

    public void Commit(int size)
    {
        if (_count == ushort.MaxValue)
        {
            throw new InvalidOperationException("A presence message holds at most 65535 entries.");
        }

        _length += size;
        _count++;
    }

    public ReadOnlySpan<byte> Finish()
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer, _length);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer[4..], EtervaleOpcodes.PresenceEntries);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[8..], PresenceWire.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[10..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer[12..], _sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[16..], (ushort)_count);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[18..], 0);
        return _buffer[.._length];
    }

    public void Reset()
    {
        _length = PresenceWire.HeaderSize;
        _count = 0;
    }
}

public static class PresenceEntriesReader
{
    public delegate void EntryHandler(in PresenceEntry entry);

    // Reads the message's sequence and hands every entry to the handler; the entries must fill the packet exactly.
    public static uint Read(ReadOnlySpan<byte> packet, EntryHandler handler)
    {
        var reader = new PacketReader(packet, PresenceWire.HeaderSize);
        if (reader.UInt16() == 0)
        {
            throw new InvalidDataException("Presence message has version 0.");
        }

        reader.Skip(2);
        var sequence = reader.UInt32();
        var count = reader.UInt16();
        reader.Skip(2);
        var offset = PresenceWire.HeaderSize;
        for (var index = 0; index < count; index++)
        {
            handler(PresenceWire.Read(packet, ref offset));
        }

        if (offset != packet.Length)
        {
            throw new InvalidDataException("Presence message has bytes after its entries.");
        }

        return sequence;
    }
}
