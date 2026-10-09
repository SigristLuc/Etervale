using System.Buffers.Binary;

namespace PristonTale.Protocol.Binary;

public enum PristonHeaderStatus
{
    NeedMoreData,
    Invalid,
    Complete
}

public static class PristonFrameCodec
{
    public const int HeaderSize = 8;
    public const int MaxFrameSize = 8192;
    public const uint EncodedPacketCode = 0x80010000;

    public static PristonHeaderStatus ReadHeader(ReadOnlySpan<byte> data, out int size, out uint code)
    {
        size = 0;
        code = 0;
        if (data.Length < HeaderSize)
        {
            return PristonHeaderStatus.NeedMoreData;
        }

        var candidateSize = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (candidateSize < HeaderSize || candidateSize > MaxFrameSize)
        {
            return PristonHeaderStatus.Invalid;
        }

        size = candidateSize;
        code = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        return PristonHeaderStatus.Complete;
    }

    public static int WriteFrame(Span<byte> destination, uint code, ReadOnlySpan<byte> payload)
    {
        var size = checked(HeaderSize + payload.Length);
        if (size > MaxFrameSize || destination.Length < size)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        BinaryPrimitives.WriteInt32LittleEndian(destination, size);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], code);
        payload.CopyTo(destination[HeaderSize..]);
        return size;
    }
}

public static class PristonPacketCipher
{
    public const int Overhead = 8;

    public static int Encode(ReadOnlySpan<byte> packet, Span<byte> destination, ushort keySeed, ushort sequence, byte xor)
    {
        if (PristonFrameCodec.ReadHeader(packet, out var length, out _) != PristonHeaderStatus.Complete || length != packet.Length)
        {
            throw new ArgumentException("Expected a complete Priston packet.", nameof(packet));
        }

        var encodedLength = checked(length + Overhead);
        if (encodedLength > PristonFrameCodec.MaxFrameSize || destination.Length < encodedLength)
        {
            throw new ArgumentOutOfRangeException(nameof(destination));
        }

        var key = PristonFrameCodec.EncodedPacketCode | keySeed;
        var sequenceKey = unchecked((key << 16) | sequence);
        var runningKey = (sequenceKey >> 4) + (uint)length;
        var previous = unchecked((byte)(sequence + key));
        ushort checksum = 0;

        BinaryPrimitives.WriteInt32LittleEndian(destination, encodedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], sequenceKey);
        for (var index = 4; index < length; index++)
        {
            var value = packet[index];
            checksum = unchecked((ushort)(checksum + value));
            var encoded = (byte)(value ^ (byte)runningKey ^ previous);
            destination[index + Overhead] = encoded;
            runningKey = unchecked(runningKey + (uint)(index * (index >> 1)));
            previous = (byte)(encoded ^ xor);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], PristonFrameCodec.EncodedPacketCode | checksum);
        return encodedLength;
    }

    public static bool TryDecode(ReadOnlySpan<byte> encoded, Span<byte> destination, byte xor, out ushort sequence, out int length)
    {
        sequence = 0;
        length = 0;
        if (PristonFrameCodec.ReadHeader(encoded, out var encodedLength, out var code) != PristonHeaderStatus.Complete
            || encodedLength != encoded.Length
            || encodedLength < PristonFrameCodec.HeaderSize + Overhead
            || (code & 0xFFFF0000u) != PristonFrameCodec.EncodedPacketCode)
        {
            return false;
        }

        var decodedLength = encodedLength - Overhead;
        if (destination.Length < decodedLength)
        {
            return false;
        }

        var sequenceKey = BinaryPrimitives.ReadUInt32LittleEndian(encoded[8..]);
        var candidateSequence = (ushort)sequenceKey;
        var runningKey = (sequenceKey >> 4) + (uint)decodedLength;
        var previous = unchecked((byte)(candidateSequence + (sequenceKey >> 16)));
        ushort checksum = 0;
        BinaryPrimitives.WriteInt32LittleEndian(destination, decodedLength);
        for (var index = 4; index < decodedLength; index++)
        {
            var encrypted = encoded[index + Overhead];
            var value = (byte)(encrypted ^ (byte)runningKey ^ previous);
            destination[index] = value;
            checksum = unchecked((ushort)(checksum + value));
            runningKey = unchecked(runningKey + (uint)(index * (index >> 1)));
            previous = (byte)(encrypted ^ xor);
        }

        if (checksum != (ushort)code
            || PristonFrameCodec.ReadHeader(destination[..decodedLength], out var innerLength, out _) != PristonHeaderStatus.Complete
            || innerLength != decodedLength)
        {
            destination[..decodedLength].Clear();
            return false;
        }

        sequence = candidateSequence;
        length = decodedLength;
        return true;
    }
}

