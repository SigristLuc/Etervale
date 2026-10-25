using System.Numerics;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// C->S {header, uint16 Version = 1, uint8 Motion (PresenceMotion stand, walk or run), uint8 0, int32 Field (-1 keeps the
// current field), int32 X, Y, Z (x256), uint16 Facing (65536 per turn), uint16 0, uint64 ActionTarget (0 none)}: the
// client's own position, sent every 200 ms while moving, on state changes and every second while idle. A field other
// than the current one asks to cross into it.
public readonly record struct MovementReportPacket(byte Motion, int Field, Vector3 Position, float Facing, ulong ActionTarget)
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4 + 4 + 12 + 4 + 8;

    public bool Moving => Motion is PresenceMotion.Walk or PresenceMotion.Run;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.MovementReport);
        writer.UInt16(CurrentVersion);
        writer.Byte(Motion);
        writer.Byte(0);
        writer.Int32(Field);
        writer.Int32(PristonUnits.ToFixed(Position.X));
        writer.Int32(PristonUnits.ToFixed(Position.Y));
        writer.Int32(PristonUnits.ToFixed(Position.Z));
        writer.UInt16(PresenceWire.ToFacing(Facing));
        writer.UInt16(0);
        writer.UInt64(ActionTarget);
        return writer.Finish();
    }

    public static MovementReportPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        if (reader.UInt16() == 0)
        {
            throw new InvalidDataException("Movement report has version 0.");
        }

        var motion = reader.Byte();
        reader.Skip(1);
        if (motion > PresenceMotion.Run)
        {
            throw new InvalidDataException("Movement report has an unknown motion.");
        }

        var field = reader.Int32();
        var position = new Vector3(PristonUnits.FromFixed(reader.Int32()), PristonUnits.FromFixed(reader.Int32()), PristonUnits.FromFixed(reader.Int32()));
        var facing = reader.UInt16() * (2f * MathF.PI / 65536f);
        reader.Skip(2);
        return new MovementReportPacket(motion, field, position, facing, reader.UInt64());
    }
}

// C->S {header, uint16 Version = 1, uint8 Flags (1 held attack), uint8 0, uint64 Target, uint64 Sequence}: the attack
// target the client clicked (0 stops attacking); an NPC target opens the NPC. The server resolves every swing itself.
public readonly record struct AttackRequestPacket(ulong Target, bool Sustained, ulong Sequence)
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4 + 8 + 8;
    private const byte SustainedFlag = 1;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.AttackRequest);
        writer.UInt16(CurrentVersion);
        writer.Byte(Sustained ? SustainedFlag : (byte)0);
        writer.Byte(0);
        writer.UInt64(Target);
        writer.UInt64(Sequence);
        return writer.Finish();
    }

    public static AttackRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        if (reader.UInt16() == 0)
        {
            throw new InvalidDataException("Attack request has version 0.");
        }

        var flags = reader.Byte();
        reader.Skip(1);
        if (flags > SustainedFlag)
        {
            throw new InvalidDataException("Attack request has unknown flags.");
        }

        return new AttackRequestPacket(reader.UInt64(), flags == SustainedFlag, reader.UInt64());
    }
}

// S->C {header, uint16 Version = 1, uint8 Flags (1 forced: a correction, teleport or restart), uint8 0, int32 Field,
// int32 X, Y, Z (x256)}: the authoritative field and position of the owner; the client puts the owner on its floor nearest
// the height Y.
public readonly record struct FieldWarpPacket(int Field, float X, float Y, float Z, bool Forced)
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4 + 4 + 12;
    private const byte ForcedFlag = 1;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.FieldWarp);
        writer.UInt16(CurrentVersion);
        writer.Byte(Forced ? ForcedFlag : (byte)0);
        writer.Byte(0);
        writer.Int32(Field);
        writer.Int32(PristonUnits.ToFixed(X));
        writer.Int32(PristonUnits.ToFixed(Y));
        writer.Int32(PristonUnits.ToFixed(Z));
        return writer.Finish();
    }

    public static FieldWarpPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        if (reader.UInt16() == 0)
        {
            throw new InvalidDataException("Field warp has version 0.");
        }

        var flags = reader.Byte();
        reader.Skip(1);
        var field = reader.Int32();
        var x = PristonUnits.FromFixed(reader.Int32());
        var y = PristonUnits.FromFixed(reader.Int32());
        return new FieldWarpPacket(field, x, y, PristonUnits.FromFixed(reader.Int32()), (flags & ForcedFlag) != 0);
    }
}
