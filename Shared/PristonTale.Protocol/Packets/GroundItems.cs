using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Ground item record: {state, type, ItemCode, x, y, z, szName[64], lpStgArea} = 92 bytes on the 32-bit wire.
// lpStgArea is an opaque server handle the client echoes back; Etervale stores the drop handle there and the
// quantity in the otherwise unused type field.
public readonly record struct GroundItemEntry(uint Handle, uint ItemCode, int Amount, int X, int Y, int Z, string Name)
{
    public const int Size = 92;

    public void Write(ref PacketWriter writer)
    {
        writer.Int32(1);
        writer.Int32(Amount);
        writer.UInt32(ItemCode);
        writer.Int32(X);
        writer.Int32(Y);
        writer.Int32(Z);
        writer.Text(Name, 64);
        writer.UInt32(Handle);
    }

    public static GroundItemEntry Read(ref PacketReader reader)
    {
        reader.Skip(4);
        var amount = reader.Int32();
        var code = reader.UInt32();
        var x = reader.Int32();
        var y = reader.Int32();
        var z = reader.Int32();
        var name = reader.Text(64);
        return new GroundItemEntry(reader.UInt32(), code, amount, x, y, z, name);
    }
}

// PLAYITEM packet: {size, code, state, Count, ground item record[Count]}, at most 16 entries.
public static class GroundItemList
{
    public const int HeaderSize = 16;
    public const int MaxItems = 16;

    public static int SizeFor(int count) => HeaderSize + count * GroundItemEntry.Size;

    public static int Write(Span<byte> destination, ReadOnlySpan<GroundItemEntry> items)
    {
        if (items.Length is < 1 or > MaxItems)
        {
            throw new ArgumentException("PLAYITEM carries between 1 and 16 items.");
        }

        var writer = new PacketWriter(destination, PristonOpcodes.PlayItem);
        writer.Int32(1);
        writer.Int32(items.Length);
        foreach (var item in items)
        {
            item.Write(ref writer);
        }

        return writer.Finish();
    }

    public static GroundItemEntry[] Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        reader.Skip(4);
        var count = reader.Int32();
        if (count is < 0 or > MaxItems || reader.Remaining < count * GroundItemEntry.Size)
        {
            throw new InvalidDataException("PLAYITEM has an invalid item count.");
        }

        var items = new GroundItemEntry[count];
        for (var index = 0; index < count; index++)
        {
            items[index] = GroundItemEntry.Read(ref reader);
        }

        return items;
    }
}

// Item action packet (DELITEM, PUTITEM request): {size, code, state, x, y, z, lpStgArea} = 28 bytes.
public readonly record struct GroundItemAction(uint Code, int State, int X, int Y, int Z, uint Handle)
{
    public const int Size = 28;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, Code);
        writer.Int32(State);
        writer.Int32(X);
        writer.Int32(Y);
        writer.Int32(Z);
        writer.UInt32(Handle);
        return writer.Finish();
    }

    public static GroundItemAction Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        return new GroundItemAction(reader.Code, reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(), reader.UInt32());
    }
}

// Chat message packet: {size, code, dwIP, dwObjectSerial, szMessage[256]}; senders use size = 32 + strlen(szMessage).
public readonly record struct ChatMessagePacket(uint Code, uint Ip, uint ObjectSerial, string Message)
{
    public const int HeaderSize = 16;
    public const int MessageLength = 256;

    public int Write(Span<byte> destination)
    {
        var bytes = PristonText.Lenient.GetBytes(Message);
        var length = Math.Min(bytes.Length, MessageLength - 1);
        var size = 32 + length;
        destination[..size].Clear();
        var writer = new PacketWriter(destination, Code);
        writer.UInt32(Ip);
        writer.UInt32(ObjectSerial);
        writer.Bytes(bytes.AsSpan(0, length));
        writer.Zero(size - writer.Position);
        return writer.Finish();
    }

    public static ChatMessagePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize + 1);
        var ip = reader.UInt32();
        var serial = reader.UInt32();
        var text = reader.Bytes(Math.Min(reader.Remaining, MessageLength));
        var terminator = text.IndexOf((byte)0);
        if (terminator < 0)
        {
            throw new InvalidDataException("Chat message text is not NUL-terminated.");
        }

        return new ChatMessagePacket(reader.Code, ip, serial, PristonText.Lenient.GetString(text[..terminator]));
    }
}
