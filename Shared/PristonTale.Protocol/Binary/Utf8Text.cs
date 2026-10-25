using System.Text;

namespace PristonTale.Protocol.Binary;

// Text of the Etervale messages: {uint16 ByteLength, UTF-8 bytes}, cut at a whole character to fit its field.
public static class Utf8Text
{
    public static void Write(ref PacketWriter writer, string? value, int maxBytes)
    {
        var text = Fit(value ?? "", maxBytes);
        writer.UInt16((ushort)Encoding.UTF8.GetByteCount(text));
        writer.Bytes(Encoding.UTF8.GetBytes(text));
    }

    public static string Read(ref PacketReader reader, int maxBytes, string what)
    {
        var length = reader.UInt16();
        if (length > maxBytes)
        {
            throw new InvalidDataException($"{what} text is longer than its field allows.");
        }

        return Encoding.UTF8.GetString(reader.Bytes(length));
    }

    public static int Size(string? value, int maxBytes) => 2 + Encoding.UTF8.GetByteCount(Fit(value ?? "", maxBytes));

    public static string Fit(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var used = 0;
        var end = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > maxBytes)
            {
                break;
            }

            used += rune.Utf8SequenceLength;
            end += rune.Utf16SequenceLength;
        }

        return value[..end];
    }
}
