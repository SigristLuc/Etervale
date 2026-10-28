using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Effects;
using PristonTale.Effects.Particles;
using PristonTale.Effects.Scripts;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Units;
using PristonTale.Assets;

namespace PristonTale.Gameplay.Effects;

public sealed record BillboardAnimation(
    IReadOnlyList<Texture2D> Textures,
    ParticleAnimation Animation);

public sealed class EffectLibrary : IDisposable
{
    private readonly PristonTextureLoader _textures;
    private readonly ContentManager _content;
    private readonly EffectDefinition[] _potions = new EffectDefinition[3];
    private readonly Dictionary<string, EffectDefinition> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BillboardAnimation> _animations = new(StringComparer.OrdinalIgnoreCase);

    private EffectLibrary(GraphicsDevice device, ContentManager content, IClientLog log)
    {
        _content = content;
        _textures = new PristonTextureLoader(device, log);
        _textures.Initialize();
    }

    // The effect definitions under Effect/Vfx, prepared on a worker from the moment the library loads.
    public VfxContent Vfx { get; private set; }
    public BillboardAnimation ReturnParticle { get; private set; }
    public BillboardAnimation ReturnRay { get; private set; }

    public EffectDefinition Potion(int index) => _potions[index];

    // The shaders the library's effects draw with come from content.
    public ContentManager Content => _content;

    public static EffectLibrary LoadBootstrap(GraphicsDevice device, ContentManager content, string assetRoot, IClientLog log)
    {
        var library = new EffectLibrary(device, content, log);
        try
        {
            library.Vfx = new VfxContent(device, content, library._textures, assetRoot, log);
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

            var returnParticle = library.LoadAnimation(assetRoot, "ReturnParticle1.ini");
            library.ReturnParticle = new BillboardAnimation(returnParticle.Textures, returnParticle.Animation);
            var returnRay = library.LoadAnimation(assetRoot, "Return1.ini");
            library.ReturnRay = new BillboardAnimation(returnRay.Textures, returnRay.Animation);
            return library;
        }
        catch
        {
            library.Dispose();
            throw;
        }
    }

    public Texture2D White => _textures.White;

    // An effect texture under the asset root, or null when the file is missing.
    public Texture2D LoadTexture(string root, string path)
    {
        return AssetFiles.Exists(Path.Combine(root, path)) ? _textures.LoadTexture(root, path) : null;
    }

    // An original particle script by name, cached; null when the script is missing or uses unsupported constructs.
    public EffectDefinition ParticleScript(string root, string name)
    {
        if (_scripts.TryGetValue(name, out var cached))
        {
            return cached;
        }

        EffectDefinition script = null;
        try
        {
            script = LoadParticleScript(root, name);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
        }

        _scripts[name] = script;
        return script;
    }

    // An original billboard animation (Effect/AnimationData/<name>) by name, cached; null when it cannot be read.
    public BillboardAnimation Animation(string root, string name)
    {
        if (_animations.TryGetValue(name, out var cached))
        {
            return cached;
        }

        BillboardAnimation animation = null;
        try
        {
            var loaded = LoadAnimation(root, name);
            animation = new BillboardAnimation(loaded.Textures, loaded.Animation);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException or FormatException)
        {
        }

        _animations[name] = animation;
        return animation;
    }

    public static IReadOnlyList<ParticleSequence> ReadParticleSequences(string root, string name)
    {
        using var stream = AssetFiles.OpenRead(Path.Combine(root, "Effect", "Particle", "Script", name + ".part"));
        using var reader = new StreamReader(stream, Encoding.Latin1);
        return PristonTale.Effects.Scripts.ParticleScript.Read(reader);
    }

    private EffectDefinition LoadParticleScript(string root, string name)
    {
        using var stream = AssetFiles.OpenRead(Path.Combine(root, "Effect", "Particle", "Script", name + ".part"));
        using var reader = new StreamReader(stream, Encoding.Latin1);
        var sequences = PristonTale.Effects.Scripts.ParticleScript.Read(reader);
        var emitters = new EmitterDefinition[sequences.Count];
        for (int index = 0; index < sequences.Count; index++)
        {
            var sequence = sequences[index];
            if (!AssetFiles.Exists(Path.Combine(root, sequence.Texture)))
            {
                throw new FileNotFoundException("Required particle texture is missing.", sequence.Texture);
            }

            emitters[index] = new EmitterDefinition
            {
                MaxParticles = sequence.Count,
                Frames = [_textures.LoadTexture(root, sequence.Texture)],
                Blend = sequence.Blend,
                ScriptSequence = sequence
            };
        }

        // Each compiled sequence owns its finite emission count and final particle lifetime.
        return new EffectDefinition { Name = name, Emitters = emitters, Duration = 0 };
    }

    // A colour-blended (blend type 2) animation whose images all exist, read through the shared AnimationData and
    // ImageData reader.
    private (Texture2D[] Textures, ParticleAnimation Animation) LoadAnimation(string root, string name)
    {
        var sequence = BillboardSequence.Read(name, path => AssetFiles.OpenRead(Path.Combine(root, path)));
        if (sequence.BlendType != 2)
        {
            throw new InvalidDataException($"Unsupported blend type in {name}.");
        }

        var textures = new Texture2D[sequence.Images.Count];
        for (var index = 0; index < textures.Length; index++)
        {
            var image = sequence.Images[index];
            if (!AssetFiles.Exists(Path.Combine(root, image)))
            {
                throw new FileNotFoundException("Required effect texture is missing.", image);
            }

            textures[index] = _textures.LoadTexture(root, image);
        }

        var frames = new ParticleAnimationFrame[sequence.Frames.Count];
        for (var index = 0; index < frames.Length; index++)
        {
            var frame = sequence.Frames[index];
            if (frame.Image < 0 || frame.Image >= textures.Length)
            {
                throw new InvalidDataException($"Animation image outside its catalog in {name}.");
            }

            frames[index] = new ParticleAnimationFrame(frame.Image, frame.DelayTicks * GameUnits.TickSeconds, frame.Alpha / 255f);
        }

        return (textures, new ParticleAnimation(frames, sequence.StartAlpha / 255f));
    }

    public void Dispose()
    {
        Vfx?.Dispose();
        _textures.Dispose();
    }
}
