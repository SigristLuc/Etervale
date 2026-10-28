using PristonTale.Protocol.Binary;
using PristonTale.Contracts.World;

namespace PristonTale.Protocol.Packets;

// C->S {header, uint16 Version = 1, uint16 0, Guid OperationId, uint8 Destination, uint8 0, uint16 0}: a player standing
// on a warp stone travels to the stone of the wing warp map at this index.
public sealed record WingGateRequestPacket(Guid OperationId, byte Destination) : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4 + 16 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.WingGateRequest);
        writer.UInt16(CurrentVersion);
        writer.UInt16(0);
        writer.Guid(OperationId);
        writer.Byte(Destination);
        writer.Zero(3);
        return writer.Finish();
    }

    public static WingGateRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        if (reader.UInt16() == 0)
        {
            throw new InvalidDataException("Wing gate request has version 0.");
        }

        reader.Skip(2);
        var operation = reader.Guid();
        return new WingGateRequestPacket(operation, reader.Byte());
    }
}

// S->C {header, uint16 Version = 1, uint16 0, Guid OperationId, uint8 Status (WingGateStatus), uint8 Destination,
// uint16 0, int64 Fee}: the answer to a wing gate trip; a trip that warped is followed by a forced FieldWarp.
public sealed record WingGateResultPacket(Guid OperationId, byte Status, byte Destination, long Fee)
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4 + 16 + 4 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.WingGateResult);
        writer.UInt16(CurrentVersion);
        writer.UInt16(0);
        writer.Guid(OperationId);
        writer.Byte(Status);
        writer.Byte(Destination);
        writer.Zero(2);
        writer.Int64(Fee);
        return writer.Finish();
    }

    public static WingGateResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        if (reader.UInt16() == 0)
        {
            throw new InvalidDataException("Wing gate result has version 0.");
        }

        reader.Skip(2);
        var operation = reader.Guid();
        var status = reader.Byte();
        var index = reader.Byte();
        reader.Skip(2);
        return new WingGateResultPacket(operation, status, index, reader.Int64());
    }
}
