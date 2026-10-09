using System.Collections.Concurrent;

namespace PristonTale.Engine3D.IO;

public static class PristonFileResolver
{
    // Cache original requests because resolution may run concurrently on the asset hot path.
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Resolve(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        if (Cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var normalized = path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var resolved = File.Exists(normalized) || Directory.Exists(normalized)
            ? normalized
            : WalkCaseInsensitive(normalized);

        if (File.Exists(resolved) || Directory.Exists(resolved))
        {
            return Cache.GetOrAdd(path, resolved);
        }

        return resolved;
    }

    public static bool Exists(string path)
    {
        var resolved = Resolve(path);
        return File.Exists(resolved) || Directory.Exists(resolved);
    }

    public static byte[] ReadAllBytes(string path) => File.ReadAllBytes(Resolve(path));

    public static Stream OpenRead(string path) => File.OpenRead(Resolve(path));

    // Rebuild real casing component by component; failures retain the requested path for diagnostics.
    private static string WalkCaseInsensitive(string normalized)
    {
        var separator = Path.DirectorySeparatorChar;
        var parts = normalized.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return normalized;

        string current;
        int startIndex;
        if (Path.IsPathRooted(normalized))
        {
            var root = Path.GetPathRoot(normalized) ?? string.Empty;
            current = root;
            var rootParts = root
                .Split(separator, StringSplitOptions.RemoveEmptyEntries)
                .Length;
            startIndex = rootParts;
        }
        else
        {
            current = ".";
            startIndex = 0;
        }

        for (var i = startIndex; i < parts.Length; i++)
        {
            var part = parts[i];
            var direct = Path.Combine(current, part);

            if (Directory.Exists(direct) || File.Exists(direct))
            {
                current = direct;
                continue;
            }

            if (!Directory.Exists(current))
                return normalized;

            var match = string.Empty;
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (string.Equals(Path.GetFileName(entry), part, StringComparison.OrdinalIgnoreCase))
                {
                    match = entry;
                    break;
                }
            }

            if (match.Length == 0)
                return normalized;

            current = match;
        }

        return current;
    }
}
