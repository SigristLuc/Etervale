using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// A line the client typed (normal chat or party chat): {size, code, char Text[]} with the terminating NUL right after
// the header, size = 9 + the text length. Unlike the lines the server sends, it carries no kind or serial.
public readonly record struct ChatInputPacket(uint Code, string Text)
{
    public const int HeaderSize = 8;

    // The text field of the classic server holds 256 bytes including the terminator.
    public const int MaxTextLength = 255;

    public int Write(Span<byte> destination)
    {
        var bytes = PristonText.Lenient.GetBytes(Text);
        var length = Math.Min(bytes.Length, MaxTextLength);
        var writer = new PacketWriter(destination, Code);
        writer.Bytes(bytes.AsSpan(0, length));
        writer.Byte(0);
        return writer.Finish();
    }

    public static ChatInputPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize + 1);
        var text = reader.Bytes(Math.Min(reader.Remaining, MaxTextLength + 1));
        var terminator = text.IndexOf((byte)0);
        if (terminator < 0)
        {
            throw new InvalidDataException("Chat input text is not NUL-terminated.");
        }

        return new ChatInputPacket(reader.Code, PristonText.Lenient.GetString(text[..terminator]));
    }
}
