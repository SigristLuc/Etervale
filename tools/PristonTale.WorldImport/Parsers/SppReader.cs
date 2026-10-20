using System.Buffers.Binary;

namespace PristonTale.WorldImport.Parsers;

/// <summary>
/// Reads one field <c>.spp</c> binary spawn-point file into the supplied <see cref="ParsedField"/>.
/// Layout: a fixed array of 200 spawn-point records <c>{ int state; int x; int z; }</c> (12 bytes each, no
/// header) = 2400 bytes total.
/// Confirmed against real assets (every <c>*.spp</c> on disk is exactly 2400 bytes).
///
/// A point is "live" when its <c>state != 0</c>; we import every record (state carried) and let the
/// factory map <c>state != 0</c> → <c>SpawnPoint.Active</c>. Coords are RAW field coords (fixed-point).
/// </summary>
public sealed class SppReader
{
    private const int RecordSize = 12;   // 3 × int32
    private const int RecordCount = 200;

    public void Read(string path, ParsedField field)
    {
        var bytes = File.ReadAllBytes(path);
        // Trust real bytes: derive the count from the file length, but never exceed the documented 200.
        var count = Math.Min(bytes.Length / RecordSize, RecordCount);
        for (var i = 0; i < count; i++)
        {
            var off = i * RecordSize;
            var state = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(off, 4));
            var x = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(off + 4, 4));
            var z = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(off + 8, 4));
            // Skip empty slots: state==0 AND x==0 AND z==0 is an unused entry (the array is fixed-size).
            if (state == 0 && x == 0 && z == 0)
                continue;
            field.SpawnPoints.Add((i, state, x, z));
        }
    }
}
