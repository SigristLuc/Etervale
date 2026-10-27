using System.Collections.Concurrent;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Assets;

namespace PristonTale.Gameplay.UI.Quests;

// The quest texts of the client's data: dialog documents (one .sin file per dialog under the quest folder) and the quest
// window's descriptions (.msg files under the quest board folder). Both are Windows-1252 text with CR LF lines. Files
// load on the thread pool; until a file is read its lines are null, and a missing file reads as no lines.
public sealed partial class HudQuestDocuments(string assetRoot, IClientLog log)
{
    public const string DialogFolder = "image/Sinimage/Quest";
    public const string BoardFolder = "image/party/party_DATA";

    private static readonly Encoding Latin = CreateEncoding();
    private readonly ConcurrentDictionary<string, string[]> _texts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _loading = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Path, int Width), string[]> _wrapped = new();

    public static string DialogPath(string document) => $"{DialogFolder}/{document}.sin";

    public static string BoardPath(string description) => $"{BoardFolder}/{description.Replace('\\', '/')}";

    // The raw lines of a file, or null while it loads.
    public string[] Lines(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return [];
        }

        if (_texts.TryGetValue(relativePath, out var lines))
        {
            return lines;
        }

        if (_loading.TryAdd(relativePath, 0))
        {
            _ = Task.Run(() => Load(relativePath));
        }

        return null;
    }

    // The lines of a file wrapped to a width in design pixels, or null while it loads.
    public string[] Wrapped(string relativePath, SpriteFont font, int width)
    {
        if (_wrapped.TryGetValue((relativePath, width), out var wrapped))
        {
            return wrapped;
        }

        var lines = Lines(relativePath);
        if (lines is null)
        {
            return null;
        }

        var result = new List<string>();
        foreach (var paragraph in Paragraphs(lines))
        {
            if (paragraph.Length == 0)
            {
                result.Add("");
                continue;
            }

            // A paragraph that fits keeps its own spacing (the columns of a stat table).
            if (font.MeasureString(paragraph).X * HudPainter.TextScale <= width)
            {
                result.Add(paragraph);
                continue;
            }

            foreach (var line in HudTextLayout.Wrap(font, paragraph, width, HudPainter.TextScale))
            {
                BreakLongLine(font, line, width, result);
            }
        }

        wrapped = result.ToArray();
        _wrapped[(relativePath, width)] = wrapped;
        return wrapped;
    }

    public bool IsLoading => !_loading.IsEmpty;

    // The files break their lines for the classic client's narrower font; consecutive lines are joined again so the text
    // wraps to the HUD's own width. A blank line ends a paragraph (several count as one); a line opening with a list mark
    // starts one; a line laid out in columns (a run of spaces inside it) or a rule of one repeated mark stands alone.
    internal static List<string> Paragraphs(IReadOnlyList<string> lines)
    {
        var paragraphs = new List<string>();
        var current = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush(paragraphs, current);
                if (paragraphs.Count > 0 && paragraphs[^1].Length > 0)
                {
                    paragraphs.Add("");
                }

                continue;
            }

            if (line.Contains("  ", StringComparison.Ordinal) || IsRule(line))
            {
                Flush(paragraphs, current);
                paragraphs.Add(IsRule(line) ? line : Columns().Replace(line, "   "));
                continue;
            }

            if (line[0] is '-' or '*' or '<' or '[' or '(' || (char.IsDigit(line[0]) && line.Length > 1 && line[1] is '.' or ')'))
            {
                Flush(paragraphs, current);
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(line);
        }

        Flush(paragraphs, current);
        if (paragraphs.Count > 0 && paragraphs[^1].Length == 0)
        {
            paragraphs.RemoveAt(paragraphs.Count - 1);
        }

        return paragraphs;
    }

    private static bool IsRule(string line) => line.Length >= 5 && !char.IsLetterOrDigit(line[0]) && line.All(character => character == line[0]);

    [System.Text.RegularExpressions.GeneratedRegex(" {3,}")]
    private static partial System.Text.RegularExpressions.Regex Columns();

    // A word wider than the line (a rule of marks) is cut to the width.
    private static void BreakLongLine(SpriteFont font, string line, int width, List<string> result)
    {
        var start = 0;
        while (start < line.Length)
        {
            var length = line.Length - start;
            while (length > 1 && font.MeasureString(line.Substring(start, length)).X * HudPainter.TextScale > width)
            {
                length--;
            }

            result.Add(line.Substring(start, length));
            start += length;
            if (IsRule(line))
            {
                return;
            }
        }
    }

    private static void Flush(List<string> paragraphs, StringBuilder current)
    {
        if (current.Length > 0)
        {
            paragraphs.Add(current.ToString());
            current.Clear();
        }
    }

    internal static string[] Decode(byte[] bytes)
    {
        var text = Latin.GetString(bytes).Replace('\t', ' ');
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var first = 0;
        while (first < lines.Length && lines[first].Trim().Length == 0)
        {
            first++;
        }

        var last = lines.Length;
        while (last > first && lines[last - 1].Trim().Length == 0)
        {
            last--;
        }

        return lines[first..last].Select(static line => line.TrimEnd()).ToArray();
    }

    private void Load(string relativePath)
    {
        string[] lines = [];
        try
        {
            var path = Path.Combine(assetRoot, relativePath);
            if (AssetFiles.Exists(path))
            {
                lines = Decode(AssetFiles.ReadAllBytes(path));
            }
            else
            {
                log.Warn($"[Quest] missing quest text {relativePath}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Error($"[Quest] cannot read {relativePath}", exception);
        }

        _texts[relativePath] = lines;
        _loading.TryRemove(relativePath, out _);
    }

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }
}
