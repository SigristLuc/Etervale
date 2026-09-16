using System.Text.Json;
using PristonTale.WorldImport.Parsers;

namespace PristonTale.WorldImport;

/// <summary>Reproducible migration data, decoded by the same parser as normal imports.</summary>
internal static class MonsterNameExport
{
    public static void Write(string sourceRoot, string outputPath)
    {
        var parser = new MonsterInfParser();
        var names = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
        var paths = Directory.EnumerateFiles(Path.Combine(sourceRoot, "Monster"), "*.inf", options)
            .OrderBy(static path => path, StringComparer.Ordinal);

        foreach (var path in paths)
        {
            var monster = parser.Parse(path);
            if (!seen.Add(monster.Name))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(monster.Name) || string.IsNullOrWhiteSpace(monster.DisplayName))
            {
                throw new InvalidDataException($"Missing monster identity or *Name in '{path}'.");
            }

            names.Add(new Entry(monster.Name, monster.DisplayName, Path.GetFileName(path)));
        }

        File.WriteAllText(outputPath, JsonSerializer.Serialize(names, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Exported {names.Count} monster names to {outputPath}.");
    }

    private sealed record Entry(string Name, string DisplayName, string SourceFile);
}
