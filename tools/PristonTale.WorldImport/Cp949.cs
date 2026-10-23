using System.Text;

namespace PristonTale.WorldImport;

// CP949 (Unified Hangul Code, code page 949) decoding for the original Korean server config files.
// The authored data files are NOT UTF-8 — every Korean token (*이름, *코드, …) is CP949.
// .NET on non-Windows does not ship code page 949 in the default encoding set, so we register
// CodePagesEncodingProvider once at startup before any read.
//
// Asset/config path handling: the original disk layout is case-stable here (these are the literal files
// in GameServer/), but callers still resolve filenames case-insensitively where they
// cross-reference a name to a path (the field ASE base ↔ .spm/.spp/.spc filename join).
public static class Cp949
{
    private static Encoding? _encoding;

    // The CP949 encoding instance (registers the code-pages provider on first use).
    public static Encoding Encoding
    {
        get
        {
            if (_encoding is not null)
                return _encoding;
            // Idempotent: registering twice is harmless. Must happen before GetEncoding(949).
            System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encoding = System.Text.Encoding.GetEncoding(949);
            return _encoding;
        }
    }

    public static string GetString(ReadOnlySpan<byte> bytes) => Encoding.GetString(bytes);

    // Read an entire CP949 text file and split it into lines (CR/LF normalised).
    public static IReadOnlyList<string> ReadLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var text = Encoding.GetString(bytes);
        // The data is read line by line: split on LF with CR stripped.
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    // Decode a fixed-length NUL-terminated CP949 field (e.g. a 32-byte name in a binary record):
    // take bytes up to the first NUL, decode as CP949, and trim trailing NULs.
    public static string GetNullTerminated(ReadOnlySpan<byte> field)
    {
        var len = field.IndexOf((byte)0);
        if (len < 0)
            len = field.Length;
        return Encoding.GetString(field[..len]);
    }
}
