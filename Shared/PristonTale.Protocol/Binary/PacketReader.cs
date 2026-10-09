using System.Buffers.Binary;

namespace PristonTale.Protocol.Binary;

// Sequential little-endian reader over one complete, size-checked packet. Overruns throw InvalidDataException.
public ref struct PacketReader
{
    private readonly ReadOnlySpan<byte> _packet;
    private int _position;

    public PacketReader(ReadOnlySpan<byte> packet, int minimumSize)
    {
        if (packet.Length < minimumSize || packet.Length < PristonFrameCodec.HeaderSize
            || BinaryPrimitives.ReadInt32LittleEndian(packet) != packet.Length)
        {
            throw new InvalidDataException($"Priston packet is shorter than {minimumSize} bytes or its size field disagrees.");
        }

        _packet = packet;
        Code = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
        _position = PristonFrameCodec.HeaderSize;
    }

    public uint Code { get; }

    public readonly int Position => _position;

    public readonly int Remaining => _packet.Length - _position;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > _packet.Length - _position)
        {
            throw new InvalidDataException("Priston packet field exceeds the packet size.");
        }

        var slice = _packet.Slice(_position, count);
        _position += count;
        return slice;
    }

    public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public short Int16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));

    public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public byte Byte() => Take(1)[0];

    public float Single() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

    public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

    public Guid Guid() => new(Take(16));

    public ReadOnlySpan<byte> Bytes(int count) => Take(count);

    public void Skip(int count) => Take(count);

    public string Text(int fieldLength) => PristonText.Read(Take(fieldLength));
}
