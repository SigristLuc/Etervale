using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Command packet: {size, code, LParam, WParam, SParam, EParam}; LParam precedes WParam on the wire.
public readonly record struct TransCommand(uint Code, int LParam = 0, int WParam = 0, int SParam = 0, int EParam = 0)
{
    public const int Size = 24;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, Code);
        writer.Int32(LParam);
        writer.Int32(WParam);
        writer.Int32(SParam);
        writer.Int32(EParam);
        return writer.Finish();
    }

    // CheckNetState5 replies keep the 24-byte body but declare the attack data size; readers accept trailing bytes.
    public static TransCommand Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        return new TransCommand(reader.Code, reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());
    }
}

// Account command packet: character command packet with char Id[32] before Name[32] = 84 bytes.
// IdGetUserInfo carries the password in Name; InsRecordData leaves LParam/SParam uninitialised in the classic client,
// so Etervale uses them for the class code and face index.
public readonly record struct CharCommand2(uint Code, int LParam, int WParam, int SParam, string Id, string Name)
{
    public const int Size = 84;
    public const int TextLength = 32;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, Code);
        writer.Int32(LParam);
        writer.Int32(WParam);
        writer.Int32(SParam);
        writer.StrictText(Id, TextLength);
        writer.StrictText(Name, TextLength);
        return writer.Finish();
    }

    public static CharCommand2 Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        return new CharCommand2(reader.Code, reader.Int32(), reader.Int32(), reader.Int32(), reader.Text(TextLength), reader.Text(TextLength));
    }
}
