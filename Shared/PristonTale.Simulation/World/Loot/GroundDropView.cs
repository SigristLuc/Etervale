using System.Numerics;

namespace PristonTale.Simulation.World.Loot;

public sealed record GroundDropView(Guid Id, uint ItemCode, string CatalogKey, string Name, int Amount, bool Gold, Vector3 Position)
{
    public uint Handle { get; init; }
}

// A client knows a drop only by its 32-bit wire handle; it names the drop with a GUID whose other 12 bytes are zero.
public static class GroundDropHandle
{
    public static Guid ToGuid(uint handle)
    {
        Span<byte> bytes = stackalloc byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, handle);
        return new Guid(bytes);
    }

    public static bool TryParse(Guid id, out uint handle)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        handle = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return handle != 0 && !bytes[4..].ContainsAnyExcept((byte)0);
    }
}

public sealed record GroundDropState(GroundDropView View, uint OwnerId, bool Public);
