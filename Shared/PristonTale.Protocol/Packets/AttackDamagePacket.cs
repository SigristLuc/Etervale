using System.Buffers.Binary;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Attack data packet sent as ATTACK_DAMAGE (92 bytes). The client computes dwChkSum with the damage checksum below,
// XOR-chains dwords 2..22 with the damage cipher keyed by its own object serial, then sends it through the encrypted
// envelope. Clicking an NPC uses the same packet with the NPC as target. Etervale ignores Power and computes damage.
public sealed record AttackDamagePacket(
    uint AttackerSerial,
    uint TargetSerial,
    int X,
    int Y,
    int Z,
    int AttackState,
    int AttackSize,
    short PowerMin,
    short PowerMax,
    int SkillCode,
    uint Time,
    int AttackCount,
    uint WeaponCode,
    short FieldCode)
{
    public const int Size = 92;
    private const uint ChecksumSalt = 0x68098051;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, PristonOpcodes.AttackDamage);
        writer.UInt32(0);
        writer.UInt32(AttackerSerial);
        writer.UInt32(TargetSerial);
        writer.Int32(X);
        writer.Int32(Y);
        writer.Int32(Z);
        writer.Int32(AttackState);
        writer.Int32(AttackSize);
        writer.Int16(PowerMin);
        writer.Int16(PowerMax);
        writer.Zero(4);
        writer.Zero(4);
        writer.Int32(SkillCode);
        writer.UInt32(Time);
        writer.Int32(AttackCount);
        writer.Zero(4);
        writer.UInt32(WeaponCode);
        writer.Int16(FieldCode);
        writer.Int16(-1);
        writer.Zero(16);
        var size = writer.Finish();
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], Checksum(destination[..size]));
        Encode(destination[..size], AttackerSerial);
        return size;
    }

    // The decoder key is the sender's serial, which the server knows from the session rather than from the payload.
    public static AttackDamagePacket Read(ReadOnlySpan<byte> encoded, uint attackerSerial)
    {
        if (encoded.Length != Size)
        {
            throw new InvalidDataException("ATTACK_DAMAGE must be exactly 92 bytes.");
        }

        Span<byte> packet = stackalloc byte[Size];
        encoded.CopyTo(packet);
        Decode(packet, attackerSerial);
        var reader = new PacketReader(packet, Size);
        var checksum = reader.UInt32();
        if (checksum != Checksum(packet))
        {
            throw new InvalidDataException("ATTACK_DAMAGE checksum mismatch.");
        }

        var attacker = reader.UInt32();
        var target = reader.UInt32();
        var x = reader.Int32();
        var y = reader.Int32();
        var z = reader.Int32();
        var state = reader.Int32();
        var size = reader.Int32();
        var powerMin = reader.Int16();
        var powerMax = reader.Int16();
        reader.Skip(8);
        var skill = reader.Int32();
        var time = reader.UInt32();
        var count = reader.Int32();
        reader.Skip(4);
        var weapon = reader.UInt32();
        var field = reader.Int16();
        return new AttackDamagePacket(attacker, target, x, y, z, state, size, powerMin, powerMax, skill, time, count, weapon, field);
    }

    // Damage checksum of the original client.
    internal static uint Checksum(ReadOnlySpan<byte> packet)
    {
        var checksum = Dword(packet, 4);
        checksum = unchecked(checksum + (Dword(packet, 20) ^ Dword(packet, 24) ^ Dword(packet, 28)));
        checksum = unchecked(checksum + (Dword(packet, 60) ^ Dword(packet, 56)));
        checksum ^= Dword(packet, 40);
        checksum ^= Dword(packet, 44);
        checksum ^= Dword(packet, 48);
        checksum ^= Dword(packet, 64);
        checksum = unchecked(checksum + Dword(packet, 52));
        return checksum ^ ChecksumSalt;
    }

    private static uint Dword(ReadOnlySpan<byte> packet, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(packet[offset..]);

    // Damage cipher: running XOR over dwords [2, size/4) keyed by the opcode and the sender's object serial.
    internal static void Encode(Span<byte> packet, uint senderSerial)
    {
        var size = BinaryPrimitives.ReadUInt32LittleEndian(packet) / 4;
        var code = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
        var running = unchecked((code ^ (code << 7)) + size * 6604);
        var shift = unchecked(code + senderSerial) & 0xFFCF;
        var step = shift & 0x3C;
        for (var index = 2; index < size; index++)
        {
            var offset = index * 4;
            running = running ^ (shift << 13) ^ BinaryPrimitives.ReadUInt32LittleEndian(packet[offset..]);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[offset..], running);
            shift = unchecked(shift + shift + step);
        }
    }

    // Inverse of the damage cipher.
    internal static void Decode(Span<byte> packet, uint senderSerial)
    {
        var size = BinaryPrimitives.ReadUInt32LittleEndian(packet) / 4;
        var code = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
        var previous = unchecked((code ^ (code << 7)) + size * 6604);
        var shift = unchecked(code + senderSerial) & 0xFFCF;
        var step = shift & 0x3C;
        for (var index = 2; index < size; index++)
        {
            var offset = index * 4;
            var encoded = BinaryPrimitives.ReadUInt32LittleEndian(packet[offset..]);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[offset..], encoded ^ (shift << 13) ^ previous);
            previous = encoded;
            shift = unchecked(shift + shift + step);
        }
    }
}
