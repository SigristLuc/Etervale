using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using SharpGLTF.Materials;

namespace PristonTale.GltfExport;

internal sealed class MaterialFactory
{
    internal const float RuntimeAlphaCutoff = 60f / 255f;

    private readonly record struct MaterialKey(
        string Texture,
        string Opacity,
        bool TwoSided,
        uint BlendType,
        float Transparency,
        bool RenderLast,
        AlphaMode AlphaMode);

    private readonly record struct ImageKey(
        string Texture,
        string Opacity,
        AlphaMode AlphaMode);

    private readonly string _assetDirectory;
    private readonly string _pngDirectory;
    private readonly bool _forceDoubleSided;
    private readonly Dictionary<MaterialKey, MaterialBuilder> _materials = [];
    private readonly Dictionary<ImageKey, string> _pngPaths = [];
    private int _missingTextures;
    private int _generatedPngs;

    public MaterialFactory(string assetDirectory, string pngDirectory, bool forceDoubleSided = false)
    {
        _assetDirectory = assetDirectory;
        _pngDirectory = pngDirectory;
        _forceDoubleSided = forceDoubleSided;
    }

    public int MissingTextures => _missingTextures;
    public int GeneratedPngs => _generatedPngs;

    public MaterialBuilder? Create(PristonMaterial material, int index)
    {
        if (material.IsInvisible || material.HasNoRenderableTexture)
        {
            return null;
        }

        var source = SelectBaseTexture(material);
        var alphaTest = UsesAlphaTest(material);
        var lateBlend = material.BlendType is 2 or 3 or 4 or 5 or 6;
        var transparent = material.RenderLast
            || (!(alphaTest && !lateBlend) && (lateBlend || material.Transparency > 0.001f));
        var alphaMode = AlphaMode.OPAQUE;
        if (transparent)
        {
            alphaMode = AlphaMode.BLEND;
        }
        else if (alphaTest)
        {
            alphaMode = AlphaMode.MASK;
        }

        var key = new MaterialKey(
            Normalize(source.TextureName),
            Normalize(source.OpacityName),
            material.TwoSided,
            material.BlendType,
            material.Transparency,
            material.RenderLast,
            alphaMode);
        if (_materials.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var pngPath = BakePng(source, index, alphaMode);
        var builder = new MaterialBuilder($"mat{index}_{Path.GetFileNameWithoutExtension(source.TextureName)}")
            .WithDoubleSide(_forceDoubleSided || material.TwoSided)
            .WithMetallicRoughnessShader()
            .WithMetallicRoughness(0f, 1f)
            .WithChannelImage(KnownChannel.BaseColor, pngPath);
        builder.GetChannel(KnownChannel.BaseColor)?.Texture?.WithSampler(
            SharpGLTF.Schema2.TextureWrapMode.REPEAT,
            SharpGLTF.Schema2.TextureWrapMode.REPEAT,
            SharpGLTF.Schema2.TextureMipMapFilter.LINEAR_MIPMAP_LINEAR,
            SharpGLTF.Schema2.TextureInterpolationFilter.LINEAR);

        if (alphaMode == AlphaMode.MASK)
        {
            builder.WithAlpha(AlphaMode.MASK, RuntimeAlphaCutoff);
        }
        else if (alphaMode == AlphaMode.BLEND)
        {
            builder.WithAlpha(AlphaMode.BLEND);
        }

        _materials.Add(key, builder);
        return builder;
    }

    private string BakePng(TextureSource source, int index, AlphaMode alphaMode)
    {
        var imageKey = new ImageKey(
            Normalize(source.TextureName),
            Normalize(source.OpacityName),
            alphaMode);
        if (_pngPaths.TryGetValue(imageKey, out var existing))
        {
            return existing;
        }

        var identity = JsonSerializer.Serialize(new
        {
            imageKey.Texture,
            imageKey.Opacity,
            Mode = (int)imageKey.AlphaMode
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var sourceName = string.IsNullOrWhiteSpace(source.TextureName)
            ? $"diffuse_{index}"
            : source.TextureName;
        var safeName = Sanitize(Path.GetFileNameWithoutExtension(sourceName.Replace('\\', '/')));
        var pngPath = Path.Combine(_pngDirectory, $"{safeName}_{hash}.png");
        var image = TryDecode(source.TextureName, out var sourcePath, out var reason);

        if (image.IsValid)
        {
            PrepareAlpha(ref image, sourcePath, source.OpacityName, alphaMode);
            PngWriter.Write(pngPath, image.Width, image.Height, image.Rgba);
        }
        else
        {
            ReportTextureIssue(source.TextureName, reason);
            WriteWhiteFallback(pngPath);
        }

        _generatedPngs++;
        _pngPaths.Add(imageKey, pngPath);
        return pngPath;
    }

    private void PrepareAlpha(
        ref DecodedImage image,
        string sourcePath,
        string opacityName,
        AlphaMode alphaMode)
    {
        if (image.HasSourceAlpha)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(opacityName))
        {
            ApplyOpacity(ref image, sourcePath, opacityName);
            return;
        }

        if (alphaMode == AlphaMode.BLEND)
        {
            SetLuminanceAlpha(image);
            return;
        }

        SetOpaque(image);
    }

    private void ApplyOpacity(ref DecodedImage image, string sourcePath, string opacityName)
    {
        var mask = TryDecode(opacityName, out var maskPath, out var reason);
        if (!mask.IsValid)
        {
            ReportTextureIssue(opacityName, reason);
            SetOpaque(image);
            return;
        }

        if (string.Equals(sourcePath, maskPath, StringComparison.OrdinalIgnoreCase))
        {
            SetOpaque(image);
            return;
        }

        if (mask.Width != image.Width || mask.Height != image.Height)
        {
            ReportTextureIssue(
                opacityName,
                $"opacity dimensions {mask.Width}x{mask.Height} do not match {image.Width}x{image.Height}");
            SetOpaque(image);
            return;
        }

        for (var index = 0; index < image.Rgba.Length; index += 4)
        {
            image.Rgba[index + 3] = (byte)((mask.Rgba[index] + mask.Rgba[index + 1] + mask.Rgba[index + 2]) / 3);
        }
    }

    private DecodedImage TryDecode(
        string textureName,
        out string resolvedPath,
        out string reason)
    {
        resolvedPath = string.Empty;
        reason = "no texture name";
        if (string.IsNullOrWhiteSpace(textureName))
        {
            return default;
        }

        resolvedPath = PristonTextureLoader.ResolvePath(_assetDirectory, textureName);
        if (resolvedPath.Length == 0)
        {
            reason = "file not found";
            return default;
        }

        var extension = Path.GetExtension(resolvedPath);
        var image = ImageDecode.Decode(File.ReadAllBytes(resolvedPath), extension);
        reason = image.IsValid ? "ok" : "decode failed";
        return image;
    }

    private static TextureSource SelectBaseTexture(PristonMaterial material)
    {
        if (!material.HasAnimatedTexture)
        {
            return new TextureSource(material.TextureName, material.OpacityName);
        }

        var frame = material.AnimationFrame == 0x100
            ? 0u
            : material.AnimationFrame;
        var index = (int)(frame % material.AnimationTextureNames.Count);
        return new TextureSource(
            material.AnimationTextureNames[index],
            string.Empty);
    }

    private static bool UsesAlphaTest(PristonMaterial material)
    {
        if (material.OpacityName.Length > 0
            || material.TextureName.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var textureName in material.AnimationTextureNames)
        {
            if (textureName.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ReportTextureIssue(string textureName, string reason)
    {
        _missingTextures++;
        if (Environment.GetEnvironmentVariable("PTGLTF_DEBUG") == "1")
        {
            Console.Error.WriteLine($"  texture fallback: '{textureName}' ({reason})");
        }
    }

    private static void SetLuminanceAlpha(DecodedImage image)
    {
        for (var index = 0; index < image.Rgba.Length; index += 4)
        {
            image.Rgba[index + 3] = Math.Max(
                image.Rgba[index],
                Math.Max(image.Rgba[index + 1], image.Rgba[index + 2]));
        }
    }

    private static void SetOpaque(DecodedImage image)
    {
        for (var index = 3; index < image.Rgba.Length; index += 4)
        {
            image.Rgba[index] = 255;
        }
    }

    private static void WriteWhiteFallback(string pngPath)
    {
        PngWriter.Write(pngPath, 1, 1, [255, 255, 255, 255]);
    }

    private static string Normalize(string name)
    {
        return name.Replace('\\', '/').ToUpperInvariant();
    }

    private static string Sanitize(string name)
    {
        var buffer = name.ToCharArray();
        var invalidCharacters = Path.GetInvalidFileNameChars();
        for (var index = 0; index < buffer.Length; index++)
        {
            if (Array.IndexOf(invalidCharacters, buffer[index]) >= 0)
            {
                buffer[index] = '_';
            }
        }

        var sanitized = new string(buffer);
        return string.IsNullOrWhiteSpace(sanitized) ? "tex" : sanitized;
    }

    private readonly record struct TextureSource(
        string TextureName,
        string OpacityName);
}
