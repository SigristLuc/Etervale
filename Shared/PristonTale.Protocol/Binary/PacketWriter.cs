using System.Buffers.Binary;

namespace PristonTale.Protocol.Binary;

// Sequential little-endian writer for x86 Priston structs. Callers size the destination from the struct layout.
public ref struct PacketWriter
{
    private readonly Span<byte> _buffer;
    private int _position;

    public PacketWriter(Span<byte> buffer, uint code)
    {
        _buffer = buffer;
        _buffer[..PristonFrameCodec.HeaderSize].Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer[4..], code);
        _position = PristonFrameCodec.HeaderSize;
    }

    public readonly int Position => _position;

    public void Int32(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer[_position..], value);
        _position += 4;
    }

    public void UInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer[_position..], value);
        _position += 4;
    }

    public void Int16(short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(_buffer[_position..], value);
        _position += 2;
    }

    public void UInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[_position..], value);
        _position += 2;
    }

    public void Byte(byte value)
    {
        _buffer[_position] = value;
        _position++;
    }

    public void Single(float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(_buffer[_position..], value);
        _position += 4;
    }

    public void Int64(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(_buffer[_position..], value);
        _position += 8;
    }

    public void Guid(Guid value)
    {
        value.TryWriteBytes(_buffer.Slice(_position, 16));
        _position += 16;
    }

    public void Bytes(ReadOnlySpan<byte> value)
    {
        value.CopyTo(_buffer[_position..]);
        _position += value.Length;
    }

    public void Zero(int count)
    {
        _buffer.Slice(_position, count).Clear();
        _position += count;
    }

    public void Text(string? value, int fieldLength)
    {
        PristonText.WriteLenient(_buffer.Slice(_position, fieldLength), value);
        _position += fieldLength;
    }

    public void StrictText(string value, int fieldLength)
    {
        PristonText.WriteStrict(_buffer.Slice(_position, fieldLength), value);
        _position += fieldLength;
    }

    public int Finish()
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer, _position);
        return _position;
    }
}
