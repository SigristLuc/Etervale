using System.Text;

namespace PristonTale.WorldImport;

/// <summary>
/// CP949 (Unified Hangul Code, code page 949) decoding for the original Korean server config files.
/// The authored data files are NOT UTF-8 — every Korean token (<c>*이름</c>, <c>*코드</c>, …) is CP949.
/// .NET on non-Windows does not ship code page 949 in the default encoding set, so we register
/// <see cref="CodePagesEncodingProvider"/> once at startup before any read.
///
/// Asset/config path handling: the original disk layout is case-stable here (these are the literal files
/// in <c>GameServer/</c>), but callers still resolve filenames case-insensitively where they
/// cross-reference a name to a path (the field ASE base ↔ .spm/.spp/.spc filename join).
/// </summary>
public static class Cp949
{
    private static Encoding? _encoding;

    /// <summary>The CP949 encoding instance (registers the code-pages provider on first use).</summary>
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

    /// <summary>Decode a CP949 byte buffer to a .NET string.</summary>
    public static string GetString(ReadOnlySpan<byte> bytes) => Encoding.GetString(bytes);

    /// <summary>Read an entire CP949 text file and split it into lines (CR/LF normalised).</summary>
    public static IReadOnlyList<string> ReadLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var text = Encoding.GetString(bytes);
        // The C++ parser reads with fgets line by line; mirror that by splitting on LF (CR stripped).
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    /// <summary>
    /// Decode a fixed-length NUL-terminated CP949 field (e.g. <c>char szName[32]</c> in a binary record):
    /// take bytes up to the first NUL, decode as CP949, and trim trailing NULs.
    /// </summary>
    public static string GetNullTerminated(ReadOnlySpan<byte> field)
    {
        var len = field.IndexOf((byte)0);
        if (len < 0)
            len = field.Length;
        return Encoding.GetString(field[..len]);
    }
}
