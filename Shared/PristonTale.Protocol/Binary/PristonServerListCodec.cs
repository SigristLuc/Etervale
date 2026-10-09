using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace PristonTale.Protocol.Binary;

public sealed record PristonServerEntry(
    string Name,
    string Ip1,
    string Ip2,
    string Ip3,
    uint Port1,
    uint Port2,
    uint Port3,
    uint ConnectionState);

public sealed record PristonServerList(
    string Name,
    uint ServerTime,
    int ClanTicket,
    int Temp,
    int GameServerCount,
    IReadOnlyList<PristonServerEntry> Entries);

public static class PristonServerListCodec
{
    public const uint PacketCode = 0x484700C0;
    public const int HeaderSize = 44;
    public const int EntrySize = 108;
    public const int MaxEntries = 8;

    public static int Write(Span<byte> destination, PristonServerList list, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(encoding);
        if (list.Entries is null || list.Entries.Count > MaxEntries || list.GameServerCount < 0 || list.GameServerCount > list.Entries.Count)
        {
            throw new ArgumentException("Invalid server list counts.", nameof(list));
        }

        var size = HeaderSize + EntrySize * list.Entries.Count;
        if (destination.Length < size)
        {
            throw new ArgumentOutOfRangeException(nameof(destination));
        }

        var packet = destination[..size];
        packet.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(packet, size);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[4..], PacketCode);
        WriteFixedString(packet.Slice(8, 16), list.Name, encoding);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[24..], list.ServerTime);
        BinaryPrimitives.WriteInt32LittleEndian(packet[28..], list.ClanTicket);
        BinaryPrimitives.WriteInt32LittleEndian(packet[32..], list.Temp);
        BinaryPrimitives.WriteInt32LittleEndian(packet[36..], list.Entries.Count - list.GameServerCount);
        BinaryPrimitives.WriteInt32LittleEndian(packet[40..], list.GameServerCount);

        for (var index = 0; index < list.Entries.Count; index++)
        {
            var entry = list.Entries[index];
            var field = packet.Slice(HeaderSize + EntrySize * index, EntrySize);
            WriteFixedString(field[..32], entry.Name, encoding);
            WriteFixedString(field.Slice(32, 20), entry.Ip1, encoding);
            WriteFixedString(field.Slice(52, 20), entry.Ip2, encoding);
            WriteFixedString(field.Slice(72, 20), entry.Ip3, encoding);
            BinaryPrimitives.WriteUInt32LittleEndian(field[92..], entry.Port1);
            BinaryPrimitives.WriteUInt32LittleEndian(field[96..], entry.Port2);
            BinaryPrimitives.WriteUInt32LittleEndian(field[100..], entry.Port3);
            BinaryPrimitives.WriteUInt32LittleEndian(field[104..], entry.ConnectionState);
        }

        return size;
    }

    public static bool TryRead(ReadOnlySpan<byte> packet, Encoding encoding, [NotNullWhen(true)] out PristonServerList? list)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        list = null;
        if (PristonFrameCodec.ReadHeader(packet, out var size, out var code) != PristonHeaderStatus.Complete
            || size != packet.Length
            || code != PacketCode
            || size < HeaderSize)
        {
            return false;
        }

        var clanCount = BinaryPrimitives.ReadInt32LittleEndian(packet[36..]);
        var gameCount = BinaryPrimitives.ReadInt32LittleEndian(packet[40..]);
        if (clanCount < 0 || gameCount < 0 || gameCount > MaxEntries || clanCount > MaxEntries - gameCount
            || size != HeaderSize + EntrySize * (clanCount + gameCount))
        {
            return false;
        }

        if (!TryReadFixedString(packet.Slice(8, 16), encoding, out var name))
        {
            return false;
        }

        var entries = new PristonServerEntry[clanCount + gameCount];
        for (var index = 0; index < entries.Length; index++)
        {
            var field = packet.Slice(HeaderSize + EntrySize * index, EntrySize);
            if (!TryReadFixedString(field[..32], encoding, out var entryName)
                || !TryReadFixedString(field.Slice(32, 20), encoding, out var ip1)
                || !TryReadFixedString(field.Slice(52, 20), encoding, out var ip2)
                || !TryReadFixedString(field.Slice(72, 20), encoding, out var ip3))
            {
                return false;
            }

            entries[index] = new PristonServerEntry(
                entryName,
                ip1,
                ip2,
                ip3,
                BinaryPrimitives.ReadUInt32LittleEndian(field[92..]),
                BinaryPrimitives.ReadUInt32LittleEndian(field[96..]),
                BinaryPrimitives.ReadUInt32LittleEndian(field[100..]),
                BinaryPrimitives.ReadUInt32LittleEndian(field[104..]));
        }

        list = new PristonServerList(
            name,
            BinaryPrimitives.ReadUInt32LittleEndian(packet[24..]),
            BinaryPrimitives.ReadInt32LittleEndian(packet[28..]),
            BinaryPrimitives.ReadInt32LittleEndian(packet[32..]),
            gameCount,
            entries);
        return true;
    }

    private static void WriteFixedString(Span<byte> destination, string value, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0'))
        {
            throw new ArgumentException("A Priston string cannot contain NUL.", nameof(value));
        }

        var bytes = encoding.GetBytes(value);
        if (bytes.Length >= destination.Length || bytes.AsSpan().Contains((byte)0) || encoding.GetString(bytes) != value)
        {
            throw new ArgumentException("A Priston string cannot be represented in its fixed field and code page.", nameof(value));
        }

        bytes.CopyTo(destination);
    }

    private static bool TryReadFixedString(ReadOnlySpan<byte> source, Encoding encoding, out string value)
    {
        var terminator = source.IndexOf((byte)0);
        if (terminator < 0)
        {
            value = string.Empty;
            return false;
        }

        var bytes = source[..terminator];
        value = encoding.GetString(bytes);
        return encoding.GetBytes(value).AsSpan().SequenceEqual(bytes);
    }
}

