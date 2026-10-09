using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.UI.Rendering;

public sealed class HudIconCache : IDisposable
{
    private sealed record PreparedIcon(string Key, DecodedPristonImage Image);

    private readonly GraphicsDevice _device;
    private readonly IClientLog _log;
    private readonly Task<Dictionary<string, string>> _index;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _requested = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _pending = new();
    private Task<PreparedIcon> _decode;
    private bool _disposed;
    private bool _failed;

    public HudIconCache(GraphicsDevice device, string assetRoot, IClientLog log)
    {
        _device = device;
        _log = log;
        _index = Task.Run(() => Index(assetRoot));
        _ = _index.ContinueWith(task => log.Error("HUD item icon index failed", task.Exception),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    public Texture2D Find(string key) => _textures.GetValueOrDefault(key);
    public bool IsIdle => _pending.Count == 0 && _decode is null;
    public bool Failed => _failed || _index.IsFaulted;

    public void Request(IEnumerable<string> keys)
    {
        // Call only when the required set changes; discard stale queued work and GPU resources.
        var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 80 || current.Count >= 96)
            {
                continue;
            }

            current.Add(key);
            if (_requested.Add(key))
            {
                _pending.Enqueue(key);
            }
        }

        foreach (var key in _textures.Keys.ToArray())
        {
            if (!current.Contains(key))
            {
                _textures[key].Dispose();
                _textures.Remove(key);
            }
        }

        _requested.IntersectWith(current);
        var pendingCount = _pending.Count;
        for (var index = 0; index < pendingCount; index++)
        {
            var key = _pending.Dequeue();
            if (current.Contains(key))
            {
                _pending.Enqueue(key);
            }
        }
    }

    public void Update()
    {
        if (_disposed || !_index.IsCompletedSuccessfully)
        {
            return;
        }

        if (_decode is not null)
        {
            if (!_decode.IsCompleted)
            {
                return;
            }

            if (_decode.IsCompletedSuccessfully)
            {
                var prepared = _decode.Result;
                if (prepared.Image.IsValid && _requested.Contains(prepared.Key))
                {
                    var image = prepared.Image;
                    var texture = new Texture2D(_device, image.Width, image.Height);
                    try
                    {
                        texture.SetData(image.Pixels);
                        if (_textures.Remove(prepared.Key, out var previous))
                        {
                            previous.Dispose();
                        }

                        _textures.Add(prepared.Key, texture);
                    }
                    catch
                    {
                        texture.Dispose();
                        throw;
                    }
                }
            }
            else
            {
                _failed = true;
            }

            _decode = null;
            return;
        }

        if (_pending.TryDequeue(out var key))
        {
            var paths = _index.Result;
            _decode = Task.Run(() => Decode(key, paths));
            _ = _decode.ContinueWith(task => _log.Error($"HUD item icon failed: {key}", task.Exception),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    private PreparedIcon Decode(string key, Dictionary<string, string> paths)
    {
        if (!paths.TryGetValue(key, out var path))
        {
            throw new FileNotFoundException($"HUD item icon is missing for key '{key}'.", key);
        }

        var image = PristonImageDecoder.Decode(PristonFileResolver.ReadAllBytes(path), ".bmp");
        if (!image.IsValid || image.Width > 256 || image.Height > 256)
        {
            throw new InvalidDataException($"Invalid HUD icon dimensions: {path}");
        }

        PristonUiPixels.Prepare(image, PristonUiColorKey.Black);

        return new PreparedIcon(key, image);
    }

    private static Dictionary<string, string> Index(string assetRoot)
    {
        var root = PristonFileResolver.Resolve(Path.Combine(assetRoot, "image", "Sinimage", "Items"));
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (!Path.GetExtension(path).Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // DropItem contains model texture sheets with names that collide with inventory sprites.
            var name = Path.GetFileNameWithoutExtension(path);
            var relative = Path.GetRelativePath(root, Path.GetDirectoryName(path));
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part.Equals("DropItem", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (name.StartsWith("it", StringComparison.OrdinalIgnoreCase) && name.Length > 2)
            {
                result.TryAdd(name[2..], path);
            }
        }

        return result;
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
        _pending.Clear();
        _decode = null;
    }
}
