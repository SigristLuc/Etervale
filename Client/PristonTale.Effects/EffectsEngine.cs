using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Effects;
using PristonTale.Effects.Rendering;

namespace PristonTale.Effects;

public sealed class EffectsEngine : IDisposable
{
    private readonly ParticleRenderer _renderer;
    private readonly List<EffectInstance> _instances = new();
    private int _spawnCounter;

    public EffectsEngine(GraphicsDevice device, ContentManager content, int seed = 12345)
    {
        ArgumentNullException.ThrowIfNull(device);
        _renderer = new ParticleRenderer(device, content);
        Seed = seed;
    }

    public int Seed { get; }

    public EffectCatalog Catalog { get; } = new();

    public IReadOnlyList<EffectInstance> Instances => _instances;

    public EffectInstance Spawn(EffectDefinition definition, Vector3 worldPosition) => Spawn(definition, worldPosition, Matrix.Identity);

    public EffectInstance Spawn(EffectDefinition definition, Vector3 worldPosition, Matrix orientation)
    {
        ArgumentNullException.ThrowIfNull(definition);
        // Derive a distinct deterministic seed per spawn from the engine seed + a monotonic counter.
        var instanceSeed = unchecked(Seed * 486187739 + (++_spawnCounter));
        var instance = new EffectInstance(definition, worldPosition, instanceSeed, orientation);
        _instances.Add(instance);
        return instance;
    }

    public EffectInstance Spawn(string name, Vector3 worldPosition)
    {
        if (!Catalog.TryGet(name, out var definition))
        {
            throw new KeyNotFoundException($"No effect named '{name}' is registered in the catalog.");
        }
        return Spawn(definition, worldPosition);
    }

    public void Update(GameTime gameTime)
    {
        ArgumentNullException.ThrowIfNull(gameTime);
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Preserve emission order: inverse-shadow and color blending are not commutative.
        for (var i = _instances.Count - 1; i >= 0; i--)
        {
            var instance = _instances[i];
            instance.Update(dt);
            if (!instance.IsAlive)
            {
                _instances.RemoveAt(i);
            }
        }
    }

    // True when the effects draw into an 8-bit target outside the renderer's HDR scene (tools and captures).
    public bool DisplayOutput
    {
        get => _renderer.DisplayOutput;
        set => _renderer.DisplayOutput = value;
    }

    public void Draw(Matrix view, Matrix projection, Vector3 cameraPosition)
        => _renderer.Draw(_instances, view, projection, cameraPosition);

    public void Clear() => _instances.Clear();

    public void Dispose()
    {
        _instances.Clear();
        _renderer.Dispose();
    }
}
