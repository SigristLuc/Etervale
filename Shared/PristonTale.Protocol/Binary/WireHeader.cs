namespace PristonTale.Protocol.Binary;

// {uint16 Version, uint16 Flags = 0} at the head of a message body. A newer version only appends fields, so a reader
// accepts its minimum version and above; the flags are reserved.
public static class WireHeader
{
    public const int Size = 4;

    public static void Write(ref PacketWriter writer, ushort version)
    {
        writer.UInt16(version);
        writer.UInt16(0);
    }

    public static void Read(ref PacketReader reader, string name, ushort minimumVersion = 1)
    {
        if (reader.UInt16() < minimumVersion)
        {
            throw new InvalidDataException(minimumVersion == 1 ? $"{name} has version 0." : $"{name} has an older layout.");
        }

        reader.Skip(2);
    }
}
