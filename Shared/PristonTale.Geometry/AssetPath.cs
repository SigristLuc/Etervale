namespace PristonTale.Geometry;

// Original asset references use Windows separators and arbitrary letter case; resolve them on any file system.
public static class AssetPath
{
    public static string? Resolve(string root, string relative)
    {
        var parts = relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var index = 0; index < parts.Length; index++)
        {
            var exact = Path.Combine(current, parts[index]);
            var last = index == parts.Length - 1;
            if (last ? File.Exists(exact) : Directory.Exists(exact))
            {
                current = exact;
                continue;
            }

            if (!Directory.Exists(current))
            {
                return null;
            }

            var candidates = last ? Directory.EnumerateFiles(current) : Directory.EnumerateDirectories(current);
            var match = candidates.FirstOrDefault(path => string.Equals(Path.GetFileName(path), parts[index], StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return null;
            }

            current = match;
        }

        return current;
    }

    // The assets directory next to the working directory or an ancestor of the executable, as the client finds it.
    public static string? FindAssetsRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "assets");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
