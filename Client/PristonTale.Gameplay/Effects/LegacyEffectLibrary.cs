using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Effects;
using PristonTale.Effects.Particles;
using PristonTale.Effects.Legacy;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.Effects;

public sealed record LegacyBillboardAnimation(
    IReadOnlyList<Texture2D> Textures,
    ParticleAnimation Animation);

public sealed class LegacyEffectLibrary : IDisposable
{
    private readonly PristonTextureLoader _textures;
    private readonly ContentManager _content;
    private readonly EffectDefinition[] _potions = new EffectDefinition[3];
    private readonly EffectDefinition[] _hulkHits = new EffectDefinition[3];

    private LegacyEffectLibrary(GraphicsDevice device, ContentManager content, IClientLog log)
    {
        _content = content;
        _textures = new PristonTextureLoader(device, log);
        _textures.Initialize();
    }

    public PristonMeshEffect HestMesh { get; private set; }
    public PristonMeshEffect HestMesh2 { get; private set; }
    public PristonMeshEffect PiercingMesh { get; private set; }
    public PristonMeshEffect ArrowMesh { get; private set; }
    public EffectDefinition CaveMummy { get; private set; }
    public EffectDefinition IcePiercing { get; private set; }
    public EffectDefinition JavelinTrail { get; private set; }
    public EffectDefinition JavelinHit { get; private set; }
    public EffectDefinition TerrainFire { get; private set; }
    public EffectDefinition FireBigHit { get; private set; }
    public LegacyBillboardAnimation ReturnParticle { get; private set; }
    public LegacyBillboardAnimation ReturnRay { get; private set; }

    public EffectDefinition Potion(int index) => _potions[index];
    public EffectDefinition HulkHit(int index) => _hulkHits[index];

    // The shaders the library's effects draw with come from content.
    public ContentManager Content => _content;

    public static LegacyEffectLibrary LoadBootstrap(GraphicsDevice device, ContentManager content, string assetRoot, IClientLog log)
    {
        var library = new LegacyEffectLibrary(device, content, log);
        try
        {
            var light = library.LoadAnimation(assetRoot, "Light1.ini");
            var halo = new EmitterDefinition
            {
                MaxParticles = 1,
                BurstCount = 1,
                Frames = light.Textures,
                Animation = light.Animation,
                Lifetime = FloatRange.Constant(light.Animation.Duration),
                Size = SizeOverLife.Constant(60f)
            };

            for (var index = 0; index < library._potions.Length; index++)
            {
                var potion = library.LoadAnimation(assetRoot, $"Potion{index + 1}.ini");
                library._potions[index] = new EffectDefinition
                {
                    Name = $"Potion{index + 1}",
                    Duration = 0.001f,
                    Emitters = [halo, new EmitterDefinition
                    {
                        MaxParticles = 30,
                        BurstCount = 30,
                        Frames = potion.Textures,
                        Animation = potion.Animation,
                        PotionMotion = true,
                        Size = SizeOverLife.Constant(4f)
                    }]
                };
            }

            for (int index = 0; index < library._hulkHits.Length; index++)
            {
                library._hulkHits[index] = library.LoadParticleScript(assetRoot, $"HulkHit{index + 1}");
            }

            var reader = new PristonSmdReader();
            PristonMeshEffect Mesh(string path, bool bones)
            {
                string full = Path.Combine(assetRoot, path);
                var mesh = reader.ReadModel(full);
                var skeleton = bones ? reader.ReadModel(Path.ChangeExtension(full, ".smb")) : mesh;
                return new PristonMeshEffect(device, library._content, library._textures, mesh, skeleton, bones ? 30 : 1);
            }

            library.HestMesh = Mesh("Effect/AssaEffect/CaveMummy/b_m_CaveMummy.smd", true);
            library.HestMesh2 = Mesh("Effect/AssaEffect/CaveMummy/b_m_CaveMummy1.smd", true);
            library.PiercingMesh = Mesh("image/Sinimage/AssaEffect/Piercing/Piercing.smd", false);
            library.ArrowMesh = Mesh("Effect/AssaEffect/Skill3Mesh/arrow.smd", false);
            library.CaveMummy = library.LoadParticleScript(assetRoot, "CaveMummy");
            library.IcePiercing = library.LoadParticleScript(assetRoot, "IceValentoPiercing");
            library.JavelinTrail = library.LoadParticleScript(assetRoot, "twistJavelin1");
            library.JavelinHit = library.LoadParticleScript(assetRoot, "TwistJavelin2");
            library.TerrainFire = library.LoadParticleScript(assetRoot, "TerrainFire");
            library.FireBigHit = library.LoadParticleScript(assetRoot, "FireBigHit1");
            var returnParticle = library.LoadAnimation(assetRoot, "ReturnParticle1.ini");
            library.ReturnParticle = new LegacyBillboardAnimation(returnParticle.Textures, returnParticle.Animation);
            var returnRay = library.LoadAnimation(assetRoot, "Return1.ini");
            library.ReturnRay = new LegacyBillboardAnimation(returnRay.Textures, returnRay.Animation);
            return library;
        }
        catch
        {
            library.Dispose();
            throw;
        }
    }

    private EffectDefinition LoadParticleScript(string root, string name)
    {
        using var stream = PristonFileResolver.OpenRead(Path.Combine(root, "Effect", "Particle", "Script", name + ".part"));
        using var reader = new StreamReader(stream, Encoding.Latin1);
        var sequences = LegacyParticleScript.Read(reader);
        var emitters = new EmitterDefinition[sequences.Count];
        for (int index = 0; index < sequences.Count; index++)
        {
            var sequence = sequences[index];
            if (!PristonFileResolver.Exists(Path.Combine(root, sequence.Texture)))
            {
                throw new FileNotFoundException("Required particle texture is missing.", sequence.Texture);
            }

            emitters[index] = new EmitterDefinition
            {
                MaxParticles = sequence.Count,
                Frames = [_textures.LoadTexture(root, sequence.Texture)],
                Blend = sequence.Blend,
                LegacySequence = sequence
            };
        }

        // Each compiled sequence owns its finite emission count and final particle lifetime.
        return new EffectDefinition { Name = name, Emitters = emitters, Duration = 0 };
    }

    private (Texture2D[] Textures, ParticleAnimation Animation) LoadAnimation(string root, string name)
    {
        var sequence = ReadSection(Path.Combine(root, "Effect", "AnimationData", name), "ANIMATION");
        if (sequence["BlendType"] != "2")
        {
            throw new InvalidDataException($"Unsupported blend type in {name}.");
        }

        var images = ReadSection(Path.Combine(root, "Effect", "ImageData", sequence["DataFile"]), "IMAGE");
        var imageCount = int.Parse(images["Count"], CultureInfo.InvariantCulture);
        if (imageCount is < 1 or > 256)
        {
            throw new InvalidDataException($"Invalid image count in {name}.");
        }

        var source = images["Name"].Replace('\\', '/');
        var extension = Path.GetExtension(source);
        var stem = source[..^extension.Length];
        var textures = new Texture2D[imageCount];
        for (var index = 0; index < textures.Length; index++)
        {
            var image = $"{stem}{index + 1}{extension}";
            if (!PristonFileResolver.Exists(Path.Combine(root, image)))
            {
                throw new FileNotFoundException("Required effect texture is missing.", image);
            }

            textures[index] = _textures.LoadTexture(root, image);
        }

        var indices = ParseNumbers(sequence["ImageNum"]);
        var delays = ParseNumbers(sequence["Delay"]);
        var alpha = ParseNumbers(sequence["BlendValue"]);
        if (indices.Length != delays.Length || indices.Length != alpha.Length)
        {
            throw new InvalidDataException($"Mismatched animation arrays in {name}.");
        }

        var frames = new ParticleAnimationFrame[indices.Length];
        for (var index = 0; index < frames.Length; index++)
        {
            if (indices[index] < 0 || indices[index] >= imageCount)
            {
                throw new InvalidDataException($"Animation image outside its catalog in {name}.");
            }

            frames[index] = new ParticleAnimationFrame(indices[index], delays[index] / 70f, alpha[index] / 255f);
        }

        var startAlpha = int.Parse(sequence["StartBlendValue"], CultureInfo.InvariantCulture) / 255f;
        return (textures, new ParticleAnimation(frames, startAlpha));
    }

    private static int[] ParseNumbers(string value)
    {
        return value.Split(',').Select(static part => int.Parse(part.Trim(), CultureInfo.InvariantCulture)).ToArray();
    }

    private static Dictionary<string, string> ReadSection(string path, string section)
    {
        using var stream = PristonFileResolver.OpenRead(path);
        // Directives and paths are ASCII; legacy comments may be CP949. Do not reinterpret their bytes as UTF-8.
        using var reader = new StreamReader(stream, Encoding.Latin1);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var active = false;
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith('['))
            {
                active = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var separator = line.IndexOf('=');
            if (active && separator > 0)
            {
                values.Add(line[..separator].Trim(), line[(separator + 1)..].Trim().Trim('"'));
            }
        }

        return values;
    }

    public void Dispose()
    {
        HestMesh?.Dispose();
        HestMesh2?.Dispose();
        PiercingMesh?.Dispose();
        ArrowMesh?.Dispose();
        _textures.Dispose();
    }
}
