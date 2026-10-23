using System.Text.Json;

namespace PristonTale.Effects.Vfx;

// The effect definitions of the game data: every *.vfx.json under <assetRoot>/Effect/Vfx, read and validated off the
// game thread. Files load in ordinal order of their relative path; a later definition of a name replaces an earlier
// one. A file that cannot be read, parsed or validated is reported and skipped as a whole.
public sealed class VfxCatalog
{
    public const string Folder = "Effect/Vfx";
    public const string Pattern = "*.vfx.json";

    private VfxCatalog(IReadOnlyList<VfxDefinition> definitions, int files, IReadOnlyList<string> errors)
    {
        Definitions = definitions;
        Files = files;
        Errors = errors;
    }

    public IReadOnlyList<VfxDefinition> Definitions { get; }
    public int Files { get; }
    public IReadOnlyList<string> Errors { get; }

    public static Task<VfxCatalog> LoadAsync(string assetRoot) => Task.Run(() => Load(assetRoot));

    public static VfxCatalog Load(string assetRoot)
    {
        var directory = Path.Combine(assetRoot, "Effect", "Vfx");
        var errors = new List<string>();
        var definitions = new List<VfxDefinition>();
        if (!Directory.Exists(directory))
        {
            return new VfxCatalog(definitions, 0, errors);
        }

        var paths = Directory.EnumerateFiles(directory, Pattern, SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: Path.GetRelativePath(directory, path).Replace('\\', '/')))
            .OrderBy(static file => file.Relative, StringComparer.Ordinal)
            .ToArray();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, relative) in paths)
        {
            VfxDefinition[] parsed;
            try
            {
                parsed = VfxDefinition.ParseFile(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                errors.Add($"{relative}: {exception.Message}");
                continue;
            }

            foreach (var definition in parsed)
            {
                if (seen.TryGetValue(definition.Name, out var earlier) && earlier != relative)
                {
                    errors.Add($"{relative}: {definition.Name} replaces the definition in {earlier}");
                }

                seen[definition.Name] = relative;
                definitions.Add(definition);
            }
        }

        return new VfxCatalog(definitions, paths.Length, errors);
    }

    // Asset paths the definitions read: textures (with numbered frames) and models with their skeletons.
    public static IEnumerable<string> Textures(VfxDefinition definition)
    {
        foreach (var emitter in definition.Emitters)
        {
            yield return emitter.Texture;
        }

        foreach (var trail in definition.Trails)
        {
            yield return trail.Texture;
        }

        foreach (var sequence in definition.Sequences)
        {
            yield return sequence.Texture;
        }

        foreach (var component in definition.Components)
        {
            if (component.Kind == VfxComponentKind.Model || component.Frames <= 0)
            {
                yield return component.Texture;
                continue;
            }

            var dot = component.Texture.LastIndexOf('.');
            var stem = dot > 0 ? component.Texture[..dot] : component.Texture;
            var extension = dot > 0 ? component.Texture[dot..] : "";
            for (var frame = 1; frame <= component.Frames; frame++)
            {
                yield return $"{stem}{frame}{extension}";
            }
        }
    }
}
