using System.Text.Json;
using PristonTale.WorldImport.Parsers;
using PristonTale.Simulation.World;

namespace PristonTale.WorldImport;

internal static class NpcPlacementExport
{
    public static void Write(string root, string output)
    {
        var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, RecurseSubdirectories = true };
        var files = Directory.EnumerateFiles(root, "*", options)
            .ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        string? Resolve(string source)
        {
            var key = source.Replace('\\', '/');
            if (key.StartsWith("GameServer/", StringComparison.OrdinalIgnoreCase))
            {
                key = key[11..];
            }

            return files.GetValueOrDefault(key);
        }

        var parser = new NpcParser();
        var reader = new SpcReader(source => Resolve(source) is { } path ? parser.Parse(path).Name : null);
        var placements = new List<Placement>();
        foreach (var definition in new WorldCatalog().AllFields)
        {
            string stage = Path.GetFileNameWithoutExtension(definition.StageSmd.Replace('\\', '/'));
            string? path = Resolve($"Field/{stage}.ase.spc") ?? Resolve($"Field/{stage}.spc");
            if (path is null)
            {
                continue;
            }

            var field = new ParsedField { AseBase = stage };
            reader.Read(path, field);
            int missing = 0;
            foreach (var placement in field.FixedNpcs)
            {
                if (Resolve(placement.SourceFile) is null)
                {
                    missing++;
                    Console.WriteLine($"Missing NPC source: field={definition.FieldCode}, file={placement.SourceFile}");
                    continue;
                }

                placements.Add(new Placement(definition.FieldCode, placement.NpcName, placement.X, placement.Z,
                    placement.Angle, placement.Y, placement.SourceFile));
            }

            Console.WriteLine($"Field {definition.FieldCode} ({stage}): {field.FixedNpcs.Count - missing} resolved, {missing} missing source files.");
        }

        File.WriteAllText(output, JsonSerializer.Serialize(placements, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Exported {placements.Count} NPC placements across all configured fields.");
    }

    private sealed record Placement(int FieldCode, string NpcName, int X, int Z, int Angle, int Y, string SourceFile);
}
