using System.Buffers.Binary;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Fixed-point and angle conventions of the original engine: positions are world units << 8,
// angles are 4096 per turn and the yaw lives in angle[1].
public static class PristonUnits
{
    public const int FixedShift = 8;
    public const float FixedScale = 256f;
    public const int AngleTurn = 4096;
    public const uint FrameNone = 0xFFFF0000;

    public static int ToFixed(float value) => (int)MathF.Round(value * FixedScale);

    public static float FromFixed(int value) => value / FixedScale;

    public static short ToAngle(float radians)
    {
        var turn = radians / (2f * MathF.PI);
        return (short)((int)MathF.Round(turn * AngleTurn) & (AngleTurn - 1));
    }

    public static float FromAngle(short angle) => (angle & (AngleTurn - 1)) * (2f * MathF.PI / AngleTurn);
}

// Original motion state codes. Coarse play-data entries carry these codes in angle[3]; Etervale uses them
// in every play buffer because the server has no per-model motion table index.
public static class MotionStateCode
{
    public const short Stand = 0x40;
    public const short Walk = 0x50;
    public const short Run = 0x60;
    public const short Attack = 0x100;
    public const short Damage = 0x110;
    public const short Dead = 0x120;
    public const short Eat = 0x140;
    public const short Skill = 0x150;
}

// Play buffer: {x, y, z, frame, short angle[4], short wStickItems[4], dwTargetSerial} = 36 bytes.
public readonly record struct PlayBuffer(int X, int Y, int Z, uint Frame, short AngleY, short Action, uint TargetSerial)
{
    public const int Size = 36;

    public void Write(ref PacketWriter writer)
    {
        writer.Int32(X);
        writer.Int32(Y);
        writer.Int32(Z);
        writer.UInt32(Frame);
        writer.Int16(0);
        writer.Int16(AngleY);
        writer.Int16(0);
        writer.Int16(Action);
        writer.Int16(-1);
        writer.Int16(-1);
        writer.Int16(-1);
        writer.Int16(-1);
        writer.UInt32(TargetSerial);
    }

    public static PlayBuffer Read(ref PacketReader reader)
    {
        var x = reader.Int32();
        var y = reader.Int32();
        var z = reader.Int32();
        var frame = reader.UInt32();
        reader.Skip(2);
        var angleY = reader.Int16();
        reader.Skip(2);
        var action = reader.Int16();
        reader.Skip(8);
        return new PlayBuffer(x, y, z, frame, angleY, action, reader.UInt32());
    }
}

// Play data packet: 44-byte header, PlayBuffCnt x {int BuffCount, play buffer}, then bEventInfo[3] effects.
// StartPosi is a ring index in the original; Etervale stores the low 32 bits of the attack request sequence there.
public sealed record TransPlayData(
    uint ObjectSerial,
    uint Target,
    int StartPosi,
    short Hp,
    short MaxHp,
    uint AutoCharCode,
    short FieldCode,
    short SecondFieldCode,
    byte DescriptorRevision,
    byte Level,
    IReadOnlyList<PlayBuffer> Buffers)
{
    public const int HeaderSize = 44;
    public const int EntrySize = 4 + PlayBuffer.Size;
    public const int MaxBuffers = 31;

    public static int SizeFor(int bufferCount) => HeaderSize + bufferCount * EntrySize;

    public int Write(Span<byte> destination)
    {
        if (Buffers.Count is < 1 or > MaxBuffers)
        {
            throw new ArgumentException("Play data packet needs between 1 and 31 play buffers.");
        }

        var writer = new PacketWriter(destination, PristonOpcodes.TransPlayData);
        writer.UInt32(ObjectSerial);
        writer.UInt32(Target);
        writer.Int32(Buffers.Count);
        writer.Int32(StartPosi);
        writer.Int16(Hp);
        writer.Int16(MaxHp);
        writer.UInt32(AutoCharCode);
        writer.Int16(FieldCode);
        writer.Int16(SecondFieldCode);
        writer.Byte(DescriptorRevision);
        writer.Zero(3);
        writer.Byte(Level);
        writer.Zero(3);
        for (var index = 0; index < Buffers.Count; index++)
        {
            writer.Int32(index);
            Buffers[index].Write(ref writer);
        }

        return writer.Finish();
    }

    public static TransPlayData Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize + EntrySize);
        var serial = reader.UInt32();
        var target = reader.UInt32();
        var count = reader.Int32();
        if (count is < 1 or > MaxBuffers || reader.Remaining < count * EntrySize)
        {
            throw new InvalidDataException("Play data packet has an invalid play-buffer count.");
        }

        var startPosi = reader.Int32();
        var hp = reader.Int16();
        var maxHp = reader.Int16();
        var autoCharCode = reader.UInt32();
        var fieldCode = reader.Int16();
        var secondFieldCode = reader.Int16();
        var revision = reader.Byte();
        reader.Skip(3);
        var level = reader.Byte();
        reader.Skip(3);
        var buffers = new PlayBuffer[count];
        for (var index = 0; index < count; index++)
        {
            reader.Skip(4);
            buffers[index] = PlayBuffer.Read(ref reader);
        }

        return new TransPlayData(serial, target, startPosi, hp, maxHp, autoCharCode, fieldCode, secondFieldCode, revision, level, buffers);
    }
}

// Coarse play data (PLAYDATA1): {size, code, dwObjectSerial, x, y, z, short angle[4], frame, dwAutoCharCode} = 40 bytes.
public readonly record struct PlayDataCoarse(uint ObjectSerial, int X, int Y, int Z, short AngleY, short Action, uint AutoCharCode)
{
    public const int Size = 40;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, PristonOpcodes.PlayData1);
        writer.UInt32(ObjectSerial);
        writer.Int32(X);
        writer.Int32(Y);
        writer.Int32(Z);
        writer.Int16(0);
        writer.Int16(AngleY);
        writer.Int16(0);
        writer.Int16(Action);
        writer.UInt32(PristonUnits.FrameNone);
        writer.UInt32(AutoCharCode);
        return writer.Finish();
    }

    public static PlayDataCoarse Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        var serial = reader.UInt32();
        var x = reader.Int32();
        var y = reader.Int32();
        var z = reader.Int32();
        reader.Skip(2);
        var angleY = reader.Int16();
        reader.Skip(2);
        var action = reader.Int16();
        reader.Skip(4);
        return new PlayDataCoarse(serial, x, y, z, angleY, action, reader.UInt32());
    }
}

// Play data group: {size, code, PlayDataCnt, dwSendCode} followed by self-sized entries.
public ref struct PlayDataGroupWriter
{
    public const int HeaderSize = 16;
    private readonly Span<byte> _buffer;
    private readonly uint _sendCode;
    private int _length;
    private int _count;

    public PlayDataGroupWriter(Span<byte> buffer, uint sendCode)
    {
        _buffer = buffer;
        _sendCode = sendCode;
        _length = HeaderSize;
        _count = 0;
    }

    public readonly int Count => _count;

    public readonly int Length => _length;

    public readonly int Free => _buffer.Length - _length;

    public Span<byte> Reserve(int size) => _buffer.Slice(_length, size);

    public void Commit(int size)
    {
        _length += size;
        _count++;
    }

    public ReadOnlySpan<byte> Finish()
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer, _length);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer[4..], PristonOpcodes.PlayDataGroup);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer[8..], _count);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer[12..], _sendCode);
        return _buffer[.._length];
    }

    public void Reset()
    {
        _length = HeaderSize;
        _count = 0;
    }
}

public static class PlayDataGroup
{
    public delegate void EntryHandler(ReadOnlySpan<byte> entry);

    public static void Read(ReadOnlySpan<byte> packet, EntryHandler handler)
    {
        var reader = new PacketReader(packet, PlayDataGroupWriter.HeaderSize);
        var count = reader.Int32();
        reader.Skip(4);
        var offset = PlayDataGroupWriter.HeaderSize;
        for (var index = 0; index < count; index++)
        {
            if (packet.Length - offset < PristonFrameCodec.HeaderSize)
            {
                throw new InvalidDataException("PLAYDATAGROUP ends inside an entry header.");
            }

            var size = BinaryPrimitives.ReadInt32LittleEndian(packet[offset..]);
            if (size < PristonFrameCodec.HeaderSize || size > packet.Length - offset)
            {
                throw new InvalidDataException("PLAYDATAGROUP entry size exceeds the packet.");
            }

            handler(packet.Slice(offset, size));
            offset += size;
        }
    }
}
