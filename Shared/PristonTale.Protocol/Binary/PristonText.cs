using System.Text;

namespace PristonTale.Protocol.Binary;

// The English client copies char[] fields with lstrcpy under the Windows ANSI code page; Western systems use 1252.
public static class PristonText
{
    public static readonly Encoding Strict = CreateEncoding(EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    public static readonly Encoding Lenient = CreateEncoding(new EncoderReplacementFallback("?"), new DecoderReplacementFallback("?"));

    private static Encoding CreateEncoding(EncoderFallback encoder, DecoderFallback decoder)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, encoder, decoder);
    }

    public static bool IsRepresentable(string value, int fieldLength)
    {
        if (value.Contains('\0'))
        {
            return false;
        }

        try
        {
            return Strict.GetByteCount(value) < fieldLength;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    public static void WriteLenient(Span<byte> field, string? value)
    {
        field.Clear();
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var bytes = Lenient.GetBytes(value);
        var length = Math.Min(bytes.Length, field.Length - 1);
        var terminator = Array.IndexOf(bytes, (byte)0, 0, length);
        if (terminator >= 0)
        {
            length = terminator;
        }

        bytes.AsSpan(0, length).CopyTo(field);
    }

    public static void WriteStrict(Span<byte> field, string value)
    {
        if (!IsRepresentable(value, field.Length))
        {
            throw new ArgumentException("The string cannot be represented in its fixed Priston field.", nameof(value));
        }

        field.Clear();
        Strict.GetBytes(value, field);
    }

    public static string Read(ReadOnlySpan<byte> field)
    {
        var terminator = field.IndexOf((byte)0);
        if (terminator < 0)
        {
            throw new InvalidDataException("A fixed Priston string field is not NUL-terminated.");
        }

        return Lenient.GetString(field[..terminator]);
    }
}
