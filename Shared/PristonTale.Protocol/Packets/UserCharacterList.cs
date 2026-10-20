using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Character slot record: 240 bytes per character slot. PosX/PosZ are fixed-point (x256) like saved positions.
// dwArmorCode carries the equipped armour item code; dwTemp[13] holds Etervale appearance fields.
public sealed record UserCharacterEntry(
    string Name,
    string ModelName,
    string ModelName2,
    uint JobCode,
    int Level,
    uint Brood,
    uint ArmorCode,
    int StartField,
    int PosX,
    int PosZ,
    int FaceIndex,
    int HairIndex,
    int Tier,
    uint WeaponCode,
    uint OffHandCode,
    bool InVillage)
{
    public const int Size = 240;

    public void Write(ref PacketWriter writer)
    {
        writer.StrictText(Name, 32);
        writer.Text(ModelName, 64);
        writer.Text(ModelName2, 64);
        writer.UInt32(JobCode);
        writer.Int32(Level);
        writer.UInt32(Brood);
        writer.UInt32(ArmorCode);
        writer.Int32(StartField);
        writer.Int32(PosX);
        writer.Int32(PosZ);
        writer.UInt32((uint)(FaceIndex & 0xFF) | (uint)(HairIndex & 0xFF) << 8);
        writer.Int32(Tier);
        writer.UInt32(WeaponCode);
        writer.UInt32(OffHandCode);
        writer.UInt32(InVillage ? 1u : 0u);
        writer.Zero(8 * 4);
    }

    public static UserCharacterEntry Read(ref PacketReader reader)
    {
        var name = reader.Text(32);
        var modelName = reader.Text(64);
        var modelName2 = reader.Text(64);
        var jobCode = reader.UInt32();
        var level = reader.Int32();
        var brood = reader.UInt32();
        var armorCode = reader.UInt32();
        var startField = reader.Int32();
        var posX = reader.Int32();
        var posZ = reader.Int32();
        var faceAndHair = reader.UInt32();
        var tier = reader.Int32();
        var weaponCode = reader.UInt32();
        var offHandCode = reader.UInt32();
        var flags = reader.UInt32();
        reader.Skip(8 * 4);
        return new UserCharacterEntry(name, modelName, modelName2, jobCode, level, brood, armorCode, startField, posX, posZ,
            (int)(faceAndHair & 0xFF), (int)(faceAndHair >> 8 & 0xFF), tier, weaponCode, offHandCode, (flags & 1) != 0);
    }
}

// Character list packet: {size, code, szID[32], PlayUserCount, character slot[6]}; always the full 1484 bytes.
public sealed record UserCharacterList(string AccountId, IReadOnlyList<UserCharacterEntry> Characters)
{
    public const int MaxCharacters = 6;
    public const int Size = 44 + MaxCharacters * UserCharacterEntry.Size;

    public int Write(Span<byte> destination)
    {
        if (Characters.Count > MaxCharacters)
        {
            throw new ArgumentException("Character list packet holds at most six characters.");
        }

        var writer = new PacketWriter(destination, PristonOpcodes.IdSetUserInfo);
        writer.Text(AccountId, 32);
        writer.Int32(Characters.Count);
        foreach (var character in Characters)
        {
            character.Write(ref writer);
        }

        writer.Zero((MaxCharacters - Characters.Count) * UserCharacterEntry.Size);
        return writer.Finish();
    }

    public static UserCharacterList Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        var account = reader.Text(32);
        var count = reader.Int32();
        if (count is < 0 or > MaxCharacters)
        {
            throw new InvalidDataException("Character list packet has an invalid character count.");
        }

        var characters = new UserCharacterEntry[count];
        for (var index = 0; index < count; index++)
        {
            characters[index] = UserCharacterEntry.Read(ref reader);
        }

        return new UserCharacterList(account, characters);
    }
}
