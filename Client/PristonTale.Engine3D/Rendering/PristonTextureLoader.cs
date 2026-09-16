using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;

namespace PristonTale.Engine3D.Rendering;

public sealed class PristonTextureLoader : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly IClientLog _log;
    private readonly ConcurrentDictionary<string, Texture2D> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<PreparedTexture>>> _preparing = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim DecodeSlots = new(2, 2);
    private Texture2D _white;
    private sealed record PreparedTexture(int Width, int Height, Color[][] Levels);

    public PristonTextureLoader(GraphicsDevice graphicsDevice, IClientLog log = null)
    {
        _graphicsDevice = graphicsDevice;
        _log = log ?? ConsoleClientLog.Instance;
    }

    public Texture2D White => _white;
    public bool RequirePreparedTextures { get; set; }

    public void Initialize()
    {
        _white = new Texture2D(_graphicsDevice, 1, 1);
        _white.SetData([Color.White]);
    }

    public Task PrepareTexturesAsync(string directory, IEnumerable<PristonMaterial> materials)
    {
        var pending = new List<Task>();
        foreach (var request in TextureRequests(materials))
        {
            pending.Add(PrepareAsync(directory, request.Name, request.Opacity));
        }

        return Task.WhenAll(pending);
    }

    internal IEnumerable<bool> UploadPreparedTextures(string directory, IEnumerable<PristonMaterial> materials)
    {
        foreach (var request in TextureRequests(materials))
        {
            var key = CacheKey(directory, request.Name, request.Opacity);
            if (_cache.ContainsKey(key))
            {
                continue;
            }

            LoadTexture(directory, request.Name, request.Opacity);
            yield return false;
        }
    }

    internal static IEnumerable<(string Name, string Opacity)> TextureRequests(IEnumerable<PristonMaterial> materials)
    {
        var seen = new HashSet<(string, string)>();
        foreach (var material in materials)
        {
            if (material.IsInvisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            if (material.HasSecondTexture && seen.Add((material.SecondTextureName, material.SecondOpacityName)))
            {
                yield return (material.SecondTextureName, material.SecondOpacityName);
            }

            if (material.HasAnimatedTexture)
            {
                foreach (var frame in material.AnimationTextureNames)
                {
                    if (seen.Add((frame, string.Empty)))
                    {
                        yield return (frame, string.Empty);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(material.TextureName) && seen.Add((material.TextureName, material.OpacityName)))
            {
                yield return (material.TextureName, material.OpacityName);
            }
        }
    }

    private Task PrepareAsync(string directory, string name, string opacity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.CompletedTask;
        }

        var key = CacheKey(directory, name, opacity);
        if (_cache.ContainsKey(key))
        {
            return Task.CompletedTask;
        }

        return _preparing.GetOrAdd(key, _ => new Lazy<Task<PreparedTexture>>(() => DecodeAsync(directory, name, opacity))).Value;
    }

    private async Task<PreparedTexture> DecodeAsync(string directory, string name, string opacity)
    {
        await DecodeSlots.WaitAsync();
        try
        {
            return await Task.Run(() => Prepare(directory, name, opacity));
        }
        finally
        {
            DecodeSlots.Release();
        }
    }

    public Texture2D LoadTexture(string assetDirectory, string textureName, string opacityName)
    {
        if (string.IsNullOrWhiteSpace(textureName))
        {
            return White;
        }

        var key = CacheKey(assetDirectory, textureName, opacityName);
        if (_cache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        PreparedTexture prepared;
        if (_preparing.TryGetValue(key, out var preparation) && preparation.Value.IsCompletedSuccessfully)
        {
            prepared = preparation.Value.Result;
        }
        else if (RequirePreparedTextures)
        {
            throw new InvalidOperationException($"Texture was not prepared before GPU upload: {textureName}");
        }
        else
        {
            // Explicit bootstrap path for loading screens and tools, never used by live streaming.
            prepared = Prepare(assetDirectory, textureName, opacityName);
        }

        var texture = Upload(key, prepared);
        _cache[key] = texture;
        _preparing.TryRemove(key, out _);
        return texture;
    }

    public Texture2D LoadTexture(string assetDirectory, string textureName)
    {
        return LoadTexture(assetDirectory, textureName, string.Empty);
    }

    public Texture2D LoadUiTexture(
        string assetDirectory,
        string textureName,
        PristonUiColorKey colorKey = PristonUiColorKey.None)
    {
        if (string.IsNullOrWhiteSpace(textureName))
        {
            return White;
        }

        var key = CacheKey(assetDirectory, textureName, string.Empty) + $"|ui:{(int)colorKey}";
        if (_cache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var prepared = PrepareUi(assetDirectory, textureName, colorKey);
        var texture = Upload(key, prepared);
        _cache[key] = texture;
        return texture;
    }

    private Texture2D Upload(string key, PreparedTexture prepared)
    {
        if (prepared.Levels.Length == 0)
        {
            return White;
        }

        var texture = new Texture2D(
            _graphicsDevice,
            prepared.Width,
            prepared.Height,
            prepared.Levels.Length > 1,
            SurfaceFormat.Color)
        {
            Name = key
        };
        try
        {
            for (var level = 0; level < prepared.Levels.Length; level++)
            {
                var pixels = prepared.Levels[level];
                texture.SetData(level, null, pixels, 0, pixels.Length);
            }
        }
        catch
        {
            texture.Dispose();
            throw;
        }

        return texture;
    }

    private PreparedTexture PrepareUi(string directory, string name, PristonUiColorKey colorKey)
    {
        var path = ResolvePath(directory, name);
        if (path.Length == 0)
        {
            _log.Warn($"UI texture '{name}' in '{directory}' is missing; using shared white fallback.");
            return new PreparedTexture(0, 0, []);
        }

        var image = ReadImage(path);
        if (!image.IsValid)
        {
            _log.Warn($"UI texture '{path}' is invalid; using shared white fallback.");
            return new PreparedTexture(0, 0, []);
        }

        PristonUiPixels.Prepare(image, colorKey);
        return new PreparedTexture(image.Width, image.Height, [image.Pixels]);
    }

    private PreparedTexture Prepare(string directory, string name, string opacity)
    {
        var path = ResolvePath(directory, name);
        if (path.Length == 0)
        {
            _log.Warn($"Texture '{name}' in '{directory}' is missing; using shared white fallback.");
            return new PreparedTexture(0, 0, []);
        }

        var image = ReadImage(path);
        if (!image.IsValid)
        {
            _log.Warn($"Texture '{path}' is invalid; using shared white fallback.");
            return new PreparedTexture(0, 0, []);
        }

        if (!image.HasSourceAlpha && !string.IsNullOrWhiteSpace(opacity))
        {
            var alphaPath = ResolvePath(directory, opacity);
            if (alphaPath.Length > 0 && !string.Equals(path, alphaPath, StringComparison.OrdinalIgnoreCase))
            {
                var mask = ReadImage(alphaPath);
                if (mask.IsValid && mask.Width == image.Width && mask.Height == image.Height)
                {
                    for (var index = 0; index < image.Pixels.Length; index++)
                    {
                        var color = image.Pixels[index];
                        var alpha = mask.Pixels[index];
                        image.Pixels[index] = new Color(color.R, color.G, color.B, (byte)((alpha.R + alpha.G + alpha.B) / 3));
                    }
                }
            }
        }

        var mipmapped = Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(opacity);
        var levels = new List<Color[]>
        {
            image.Pixels
        };
        var width = image.Width;
        var height = image.Height;
        var previous = image.Pixels;
        while (mipmapped && (width > 1 || height > 1))
        {
            var nextWidth = Math.Max(1, width / 2);
            var nextHeight = Math.Max(1, height / 2);
            var next = new Color[nextWidth * nextHeight];
            for (var y = 0; y < nextHeight; y++)
            {
                for (var x = 0; x < nextWidth; x++)
                {
                    var x0 = x * 2;
                    var x1 = Math.Min(x0 + 1, width - 1);
                    var y0 = y * 2;
                    var y1 = Math.Min(y0 + 1, height - 1);
                    var a = previous[y0 * width + x0];
                    var b = previous[y0 * width + x1];
                    var c = previous[y1 * width + x0];
                    var d = previous[y1 * width + x1];
                    next[y * nextWidth + x] = new Color(
                        (byte)((a.R + b.R + c.R + d.R) / 4),
                        (byte)((a.G + b.G + c.G + d.G) / 4),
                        (byte)((a.B + b.B + c.B + d.B) / 4),
                        (byte)((a.A + b.A + c.A + d.A) / 4));
                }
            }

            levels.Add(next);
            previous = next;
            width = nextWidth;
            height = nextHeight;
        }

        return new PreparedTexture(image.Width, image.Height, levels.ToArray());
    }

    private static DecodedPristonImage ReadImage(string path)
    {
        return PristonImageDecoder.Decode(PristonFileResolver.ReadAllBytes(path), Path.GetExtension(path));
    }

    private static string CacheKey(string directory, string name, string opacity)
    {
        return Path.GetFullPath(directory).Replace('\\', '/') + "|" + name.Replace('\\', '/') + "|" + opacity.Replace('\\', '/');
    }

    internal static string ResolvePath(string directory, string name)
    {
        var normalized = name.Replace('\\', Path.DirectorySeparatorChar);
        var resolved = PristonFileResolver.Resolve(Path.Combine(directory, normalized));
        if (File.Exists(resolved))
        {
            return resolved;
        }

        var stem = Path.Combine(directory, Path.GetFileNameWithoutExtension(normalized));
        foreach (var extension in new[]
        {
            ".bmp",
            ".tga",
            ".jpg",
            ".jpeg"
        }

        )
        {
            var candidate = PristonFileResolver.Resolve(stem + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    public void Dispose()
    {
        foreach (var texture in _cache.Values)
        {
            if (!ReferenceEquals(texture, _white))
            {
                texture.Dispose();
            }
        }

        _cache.Clear();
        _preparing.Clear();
        _white?.Dispose();
    }
}
