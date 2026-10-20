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

    // CHECK_NETSTATE5 replies keep the 24-byte body but declare the attack data size; readers accept trailing bytes.
    public static TransCommand Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        return new TransCommand(reader.Code, reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());
    }
}

// Extended command packet: command packet followed by LxParam, WxParam, SxParam, ExParam.
public readonly record struct TransCommandEx(uint Code, int LParam, int WParam, int SParam, int EParam, int LxParam, int WxParam, int SxParam, int ExParam)
{
    public const int Size = 40;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, Code);
        writer.Int32(LParam);
        writer.Int32(WParam);
        writer.Int32(SParam);
        writer.Int32(EParam);
        writer.Int32(LxParam);
        writer.Int32(WxParam);
        writer.Int32(SxParam);
        writer.Int32(ExParam);
        return writer.Finish();
    }

    public static TransCommandEx Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        return new TransCommandEx(reader.Code, reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(),
            reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());
    }
}

// Character command packet: {size, code, LParam, WParam, SParam, char szName[32]} = 52 bytes.
public readonly record struct CharCommand(uint Code, int LParam, int WParam, int SParam, string Name)
{
    public const int Size = 52;
    public const int NameLength = 32;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, Code);
        writer.Int32(LParam);
        writer.Int32(WParam);
        writer.Int32(SParam);
        writer.StrictText(Name, NameLength);
        return writer.Finish();
    }

    public static CharCommand Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        return new CharCommand(reader.Code, reader.Int32(), reader.Int32(), reader.Int32(), reader.Text(NameLength));
    }
}

// Account command packet: character command packet with szId[32] before szName[32] = 84 bytes.
// ID_GETUSERINFO carries the password in szName; INSRECORDDATA leaves LParam/SParam uninitialised in the original,
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
