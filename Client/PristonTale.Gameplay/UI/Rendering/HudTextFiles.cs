using System.Collections.Concurrent;
using System.Text;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Assets;
using PristonTale.Engine3D.IO;

namespace PristonTale.Gameplay.UI.Rendering;

// Text files of the client's data in one encoding, read on the thread pool and kept: until a file is read its lines are
// null, and a missing or unreadable file reads as no lines. Tabs become spaces, lines lose their trailing spaces and the
// text its blank lines before and after.
public sealed class HudTextFiles(string assetRoot, Encoding encoding, IClientLog log, string tag)
{
    private readonly ConcurrentDictionary<string, string[]> _texts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _loading = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLoading => !_loading.IsEmpty;

    // The lines of a file, or null while it loads.
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

    public static string[] Decode(byte[] bytes, Encoding encoding)
    {
        var text = encoding.GetString(bytes).Replace('\t', ' ');
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
                lines = Decode(AssetFiles.ReadAllBytes(path), encoding);
            }
            else
            {
                log.Warn($"[{tag}] missing text {relativePath}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Error($"[{tag}] cannot read {relativePath}", exception);
        }

        _texts[relativePath] = lines;
        _loading.TryRemove(relativePath, out _);
    }
}
