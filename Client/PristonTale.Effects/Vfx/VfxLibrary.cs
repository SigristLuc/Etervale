using System.Text.Json;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Effects.Vfx;

// Named effects compiled against their textures. Definitions come from authored .vfx.json files; a name registered
// twice keeps the later definition.
public sealed class VfxLibrary
{
    private readonly Dictionary<string, CompiledVfx> _effects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, Texture2D?> _textures;
    private readonly Texture2D _fallback;
    private readonly VfxBuiltinTextures? _builtins;

    // textures resolves an asset-relative path; fallback is used when it returns null (a missing texture never hides
    // the effect's lights and sounds). "builtin:" paths resolve to the procedural textures.
    public VfxLibrary(Func<string, Texture2D?> textures, Texture2D fallback, VfxBuiltinTextures? builtins = null)
    {
        _textures = textures;
        _fallback = fallback;
        _builtins = builtins;
    }

    public int Count => _effects.Count;

    public IReadOnlyCollection<string> Names => _effects.Keys;

    public CompiledVfx Register(VfxDefinition definition)
    {
        definition.Validate();
        var emitters = definition.Emitters.Select(emitter => new CompiledEmitter(emitter, Texture(emitter.Texture))).ToArray();
        var trails = definition.Trails.Select(trail => new CompiledTrail(trail, Texture(trail.Texture))).ToArray();
        var compiled = new CompiledVfx(definition, emitters, trails);
        _effects[definition.Name] = compiled;
        return compiled;
    }

    // Authored files are optional: a file that cannot be read or parsed is reported and skipped as a whole.
    public int LoadDirectory(string directory, Action<string> reportError)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var count = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.vfx.json").Order(StringComparer.Ordinal))
        {
            VfxDefinition[] definitions;
            try
            {
                definitions = VfxDefinition.ParseFile(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                reportError($"{path}: {exception.Message}");
                continue;
            }

            foreach (var definition in definitions)
            {
                Register(definition);
                count++;
            }
        }

        return count;
    }

    public bool TryGet(string name, out CompiledVfx effect) => _effects.TryGetValue(name, out effect!);

    private Texture2D Texture(string path)
    {
        if (path.Length == 0)
        {
            return _fallback;
        }

        if (_builtins is not null && _builtins.TryGet(path, out var builtin))
        {
            return builtin;
        }

        return path.StartsWith(VfxBuiltinTextures.Prefix, StringComparison.OrdinalIgnoreCase) ? _fallback : _textures(path) ?? _fallback;
    }
}
