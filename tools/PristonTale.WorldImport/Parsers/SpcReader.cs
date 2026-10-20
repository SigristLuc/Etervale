using System.Buffers.Binary;

namespace PristonTale.WorldImport.Parsers;

/// <summary>
/// Reads one field <c>.spc</c> binary fixed-NPC file into the supplied <see cref="ParsedField"/>.
/// Layout: a fixed array of 100 player-info records, the same 504-byte record as the PLAYERINFO packet
/// (no header) = 50400 bytes. Confirmed against real
/// assets (every <c>*.spc</c> on disk is exactly 50400 bytes).
///
/// Verified byte offsets within a 504-byte record (32-bit Win32 layout, default packing):
/// <list type="bullet">
///   <item><c>int size</c> @0, <c>int code</c> @4 (presence flag — record live when <c>code != 0</c>).</item>
///   <item>embedded character info record @8; its <c>char szName[32]</c> is at record offset 8 (historical display name).</item>
///   <item><c>int x,y,z</c> @476/480/484; <c>int ax,ay,az</c> @488/492/496 (facing; <c>ay</c> is the heading).</item>
/// </list>
/// Resolve the config path at offset 104 through the NPC parser, as the original server does when it opens fixed NPCs; saved names may be stale. Preserve x/y/z and heading.
/// </summary>
public sealed class SpcReader
{
    private readonly Func<string, string?>? _resolveName;

    public SpcReader(Func<string, string?>? resolveName = null) => _resolveName = resolveName;

    private const int RecordSize = 504;
    private const int RecordCount = 100;

    private const int OffCode = 4;
    private const int OffName = 8;     // char szName[32] (character info record + 0)
    private const int NameLen = 32;
    private const int OffX = 476;
    private const int OffZ = 484;
    private const int OffAngleY = 492; // ay — the usual heading

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
