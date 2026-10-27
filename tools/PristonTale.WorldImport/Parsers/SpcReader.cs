using System.Buffers.Binary;
using PristonTale.Assets;

namespace PristonTale.WorldImport.Parsers;

// Reads one field .spc binary fixed-NPC file into the supplied ParsedField.
// Layout: a fixed array of 100 player-info records, the same 504-byte record as the PlayerInfo packet
// (no header) = 50400 bytes. Confirmed against real
// assets (every *.spc on disk is exactly 50400 bytes).
//
// Verified byte offsets within a 504-byte record (32-bit Win32 layout, default packing):
//
//   - int size @0, int code @4 (presence flag — record live when code != 0).
//   - embedded character info record @8; its 32-byte name is at record offset 8 (historical display name).
//   - int x,y,z @476/480/484; int ax,ay,az @488/492/496 (facing; ay is the heading).
//
// Resolve the config path at offset 104 through the NPC parser, as the original server does when it opens fixed NPCs; saved names may be stale. Preserve x/y/z and heading.
public sealed class SpcReader
{
    private readonly Func<string, string?>? _resolveName;

    public SpcReader(Func<string, string?>? resolveName = null) => _resolveName = resolveName;

    private const int RecordSize = 504;
    private const int RecordCount = 100;

    private const int OffCode = 4;
    private const int OffName = 8;     // 32-byte name (character info record + 0)
    private const int NameLen = 32;
    private const int OffX = 476;
    private const int OffZ = 484;
    private const int OffAngleY = 492; // ay — the usual heading

    // Configuration paths of the live records, in file order.
    public static IReadOnlyList<string> Sources(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var count = Math.Min(bytes.Length / RecordSize, RecordCount);
        var sources = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var off = i * RecordSize;
            if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(off + OffCode, 4)) != 0)
            {
                sources.Add(Cp949.GetNullTerminated(bytes.AsSpan(off + 104, 60)));
            }
        }

        return sources;
    }

    public void Read(string path, ParsedField field)
    {
        var bytes = File.ReadAllBytes(path);
        var count = Math.Min(bytes.Length / RecordSize, RecordCount);
        for (var i = 0; i < count; i++)
        {
            var off = i * RecordSize;
            var code = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(off + OffCode, 4));
            if (code == 0) // empty slot
            {
                continue;
            }

            var source = Cp949.GetNullTerminated(bytes.AsSpan(off + 104, 60));
            var name = _resolveName?.Invoke(source) ?? Cp949.GetNullTerminated(bytes.AsSpan(off + OffName, NameLen)).Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var x = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(off + OffX, 4));
            var y = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(off + 480, 4));
            var z = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(off + OffZ, 4));
            var angle = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(off + OffAngleY, 4));
            field.FixedNpcs.Add(new ParsedNpcPlacement(name, x, z, angle, y, source));
        }
    }
}
