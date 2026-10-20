namespace PristonTale.WorldImport;

/// <summary>
/// The shared line/token reader for the <c>*token value...</c> text format of the authored config files.
/// Mirrors the original tokenizer faithfully:
/// <list type="bullet">
///   <item>Words: skip leading SPACE(0x20)/TAB(0x09), copy bytes until the next SPACE/TAB or CR/LF — i.e.
///   <b>whitespace-split, no comment character</b>.</item>
///   <item>Quoted strings: scan to the first <c>"</c>, copy until the closing <c>"</c>. The universal name idiom is: read one word; <b>if it began with <c>"</c></b>,
///   re-parse from the saved start as a quoted string (so names with spaces survive).</item>
/// </list>
/// There is no comment marker in the format: a <c>//</c> line simply has a leading token that matches
/// nothing and is ignored. We reproduce that by only acting on recognised <c>*</c>/<c>**</c> keys.
///
/// A <see cref="Tokenizer"/> wraps a single line; <see cref="NextWord"/> walks words left-to-right and
/// <see cref="QuotedFrom"/> re-reads a quoted string spanning embedded spaces.
/// </summary>
public sealed class Tokenizer
{
    private readonly string _line;
    private int _pos;

    public Tokenizer(string line)
    {
        _line = line ?? string.Empty;
        _pos = 0;
    }

    /// <summary>True once the cursor has consumed the whole line.</summary>
    public bool AtEnd
    {
        get
        {
            var p = _pos;
            while (p < _line.Length && IsSpace(_line[p]))
                p++;
            return p >= _line.Length;
        }
    }

    /// <summary>The leading word (the <c>*</c>/<c>**</c> key) without disturbing the cursor for re-reads.</summary>
    public static string LeadingKey(string line)
    {
        var t = new Tokenizer(line);
        return t.NextWord();
    }

    /// <summary>
    /// Read the next whitespace-delimited word (GetWord). Returns the empty string at end of line.
    /// </summary>
    public string NextWord()
    {
        while (_pos < _line.Length && IsSpace(_line[_pos]))
            _pos++;
        var start = _pos;
        while (_pos < _line.Length && !IsSpace(_line[_pos]))
            _pos++;
        return _line[start.._pos];
    }

    /// <summary>
    /// Read a value that may be a quoted string: peek the next word; if it starts with <c>"</c>, re-read
    /// the quoted span (which may contain spaces). Otherwise return the plain word. Mirrors the C++
    /// <c>p=GetWord(buf,p); if(buf[0]=='"') p=GetString(buf,pb);</c> idiom.
    /// </summary>
    public string NextValueOrQuoted()
    {
        // Skip leading whitespace, remember the start so a quoted re-read sees the opening quote.
        while (_pos < _line.Length && IsSpace(_line[_pos]))
            _pos++;
        if (_pos >= _line.Length)
            return string.Empty;

        if (_line[_pos] == '"')
            return ReadQuoted();

        var start = _pos;
        while (_pos < _line.Length && !IsSpace(_line[_pos]))
            _pos++;
        return _line[start.._pos];
    }

    /// <summary>Read a quoted string from the cursor (cursor must be at the opening quote or whitespace).</summary>
    private string ReadQuoted()
    {
        // Find opening quote.
        while (_pos < _line.Length && _line[_pos] != '"')
            _pos++;
        if (_pos >= _line.Length)
            return string.Empty;
        _pos++; // skip opening quote
        var start = _pos;
        while (_pos < _line.Length && _line[_pos] != '"')
            _pos++;
        var value = _line[start.._pos];
        if (_pos < _line.Length)
            _pos++; // skip closing quote
        return value;
    }

    /// <summary>Read the next word as an integer (atoi semantics: empty/invalid → 0).</summary>
    public int NextInt() => Atoi(NextWord());

    /// <summary>Read the next word as a float (atof semantics: empty/invalid → 0).</summary>
    public float NextFloat() => Atof(NextWord());

    /// <summary>Collect every remaining whitespace-delimited word (used for code lists, job names).</summary>
    public IReadOnlyList<string> RemainingWords()
    {
        var words = new List<string>();
        while (true)
        {
            var w = NextWord();
            if (w.Length == 0)
                break;
            words.Add(w);
        }
        return words;
    }

    public IReadOnlyList<string> RemainingValues()
    {
        var values = new List<string>();
        while (!AtEnd)
        {
            values.Add(NextValueOrQuoted());
        }

        return values;
    }

    // atoi/atof tolerate trailing garbage and leading sign/whitespace and return 0 on no-leading-digit.
    public static int Atoi(string s)
    {
        s = s.Trim();
        if (s.Length == 0)
            return 0;
        var i = 0;
        var sign = 1;
        if (s[0] is '+' or '-')
        {
            if (s[0] == '-') sign = -1;
            i++;
        }
        long value = 0;
        var any = false;
        for (; i < s.Length && char.IsDigit(s[i]); i++)
        {
            value = value * 10 + (s[i] - '0');
            any = true;
        }
        return any ? (int)(sign * value) : 0;
    }

    public static float Atof(string s)
    {
        s = s.Trim();
        if (s.Length == 0)
            return 0f;
        // Read the leading numeric prefix (C's atof tolerates trailing non-numerics).
        var end = 0;
        var seenDot = false;
        if (end < s.Length && (s[end] == '+' || s[end] == '-'))
            end++;
        while (end < s.Length && (char.IsDigit(s[end]) || (s[end] == '.' && !seenDot)))
        {
            if (s[end] == '.') seenDot = true;
            end++;
        }
        var prefix = s[..end];
        return float.TryParse(prefix, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\r' or '\n';
}
