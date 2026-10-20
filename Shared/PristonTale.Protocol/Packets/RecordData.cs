using System.Buffers.Binary;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Character record: header "RC 1.50", character info record at 16, game save block (512 bytes)
// at 480, 64 thrown-item records at 992, then ThrowItemCount, ItemCount, ItemSubStart, DataSize and the item blob at 1776.
// Etervale keeps inventory server-side and sends it with EtervaleOpcodes.Inventory, so ItemCount and DataSize are zero.
public sealed record RecordDataPacket(CharacterInfo Character, int FieldCode, int PosX, int PosZ)
{
    public const int FixedSize = 1776;
    private const int SaveInfoOffset = 480;
    private static ReadOnlySpan<byte> Header => "RC 1.50\0"u8;

    public byte[] Build()
    {
        var buffer = new byte[FixedSize];
        var writer = new PacketWriter(buffer, PristonOpcodes.RecordData);
        writer.Bytes(Header);
        Character.Write(ref writer);
        writer.Int32(0);
        writer.Int32(FieldCode);
        writer.Int32(0);
        writer.Int32(PosX);
        writer.Int32(PosZ);
        writer.Zero(FixedSize - writer.Position);
        writer.Finish();
        return buffer;
    }

    public static RecordDataPacket Read(ReadOnlySpan<byte> record)
    {
        var reader = new PacketReader(record, FixedSize);
        if (reader.Code != PristonOpcodes.RecordData || !reader.Bytes(8).SequenceEqual(Header))
        {
            throw new InvalidDataException("Character record has an unexpected code or version header.");
        }

        var character = CharacterInfo.Read(ref reader);
        if (reader.Position != SaveInfoOffset)
        {
            throw new InvalidOperationException("Character record layout drifted.");
        }

        reader.Skip(4);
        var field = reader.Int32();
        reader.Skip(4);
        var posX = reader.Int32();
        var posZ = reader.Int32();
        return new RecordDataPacket(character, field, posX, posZ);
    }
}

// Record chunk packet: {size, code, Count, Total, TransSize, szData[8160]}. Each chunk declares TransSize + 32 bytes
// (12 bytes past the data are padding). Total is size/8160 plus one for a remainder only when size > 8160, so a
// record that fits one chunk declares Total = 0.
public static class RecordDataChunks
{
    public const int ChunkData = 8160;
    public const int ChunkHeader = 20;
    public const int ChunkOverhead = 32;

    public static IEnumerable<byte[]> Split(byte[] record)
    {
        var size = record.Length;
        var total = size / ChunkData + (size % ChunkData != 0 && size > ChunkData ? 1 : 0);
        var count = 0;
        for (var offset = 0; offset < size; offset += ChunkData)
        {
            var length = Math.Min(ChunkData, size - offset);
            var chunk = new byte[length + ChunkOverhead];
            BinaryPrimitives.WriteInt32LittleEndian(chunk, chunk.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), PristonOpcodes.RecordData);
            BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(8), count);
            BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(12), total);
            BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(16), length);
            record.AsSpan(offset, length).CopyTo(chunk.AsSpan(ChunkHeader));
            count++;
            yield return chunk;
        }
    }
}

public sealed class RecordDataAssembler
{
    private const int MaxRecordSize = 1776 + 548 * 200;
    private readonly SortedDictionary<int, byte[]> _chunks = new();
    private int _total = -1;

    // Returns the complete record once every chunk has arrived; null while parts are still missing.
    public byte[]? Add(ReadOnlySpan<byte> chunk)
    {
        var reader = new PacketReader(chunk, RecordDataChunks.ChunkHeader);
        var count = reader.Int32();
        var total = reader.Int32();
        var length = reader.Int32();
        if (count < 0 || total < 0 || length < 0 || length > reader.Remaining || length > RecordDataChunks.ChunkData
            || (long)(Math.Max(total, 1)) * RecordDataChunks.ChunkData > MaxRecordSize + RecordDataChunks.ChunkData
            || _total >= 0 && total != _total || (total > 0 ? count >= total : count != 0))
        {
            throw new InvalidDataException("RECORDDATA chunk has inconsistent indices.");
        }

        _total = total;
        _chunks[count] = reader.Bytes(length).ToArray();
        if (_chunks.Count < Math.Max(total, 1))
        {
            return null;
        }

        var record = _chunks.Values.SelectMany(part => part).ToArray();
        _chunks.Clear();
        _total = -1;
        return record;
    }
}
