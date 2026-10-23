using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace PristonTale.Effects.Vfx;

// Authoring format of Etervale's skill and combat effects (Content/Vfx/*.vfx.json). An effect is a set of layers that
// share one clock: sprite emitters, ribbon trails, transient lights, sounds and, for the original effects, legacy
// particle scripts played unchanged. Colours are linear Rec.709; Intensity scales them into the HDR scene relative to
// the current exposure, so 1 displays the colour as authored and higher values bloom.
public enum VfxAnchor
{
    // The position the effect was spawned at (an impact point, a ground target).
    Point,
    Caster,
    Target
}

public enum VfxBlend
{
    Additive,
    Alpha
}

public enum VfxShape
{
    Point,
    Sphere,
    Ring,
    Disc
}

public enum VfxAlign
{
    // Camera-facing billboard.
    Camera,
    // Flat on the ground plane (shock rings, magic circles).
    Ground,
    // Stretched along the particle's velocity (sparks, streaks).
    Velocity
}

public sealed record VfxKey(float T, float[] V);

public sealed record VfxEmitterDefinition
{
    public string Texture { get; init; } = "";
    // Sprite-sheet frames (columns x rows) played over the particle's life.
    public int FrameColumns { get; init; } = 1;
    public int FrameRows { get; init; } = 1;
    public VfxBlend Blend { get; init; } = VfxBlend.Additive;
    public float Intensity { get; init; } = 1f;
    // Fade distance in world units against the scene depth; 0 disables the soft-particle fade.
    public float Softness { get; init; } = 16f;
    public VfxAnchor Anchor { get; init; } = VfxAnchor.Point;
    // Bone of the anchor's actor ("Bip weapon01", "Bip01 R Hand"); empty uses the anchor's position.
    public string Bone { get; init; } = "";
    public float[] Offset { get; init; } = [0f, 0f, 0f];
    // Particles follow a moving anchor instead of staying where they were born.
    public bool Local { get; init; }
    public float Delay { get; init; }
    // Seconds of continuous emission at Rate; 0 emits for the whole effect duration.
    public float EmitTime { get; init; }
    public float Rate { get; init; }
    public int Burst { get; init; }
    public int MaxParticles { get; init; } = 64;
    public float[] Lifetime { get; init; } = [1f, 1f];
    public float[] Speed { get; init; } = [0f, 0f];
    public float[] Direction { get; init; } = [0f, 1f, 0f];
    // Cone half-angle around Direction in degrees; 180 emits in every direction.
    public float Spread { get; init; }
    public VfxShape Shape { get; init; } = VfxShape.Point;
    public float[] Radius { get; init; } = [0f, 0f];
    public float[] Gravity { get; init; } = [0f, 0f, 0f];
    // Fraction of velocity lost per second.
    public float Drag { get; init; }
    // Size over normalized life: keys {t, [size]}.
    public VfxKey[] Size { get; init; } = [new(0f, [10f])];
    // Colour over normalized life: keys {t, [r, g, b, a]}.
    public VfxKey[] Color { get; init; } = [new(0f, [1f, 1f, 1f, 1f])];
    public float[] Rotation { get; init; } = [0f, 0f];
    // Degrees per second.
    public float[] Spin { get; init; } = [0f, 0f];
    public VfxAlign Align { get; init; } = VfxAlign.Camera;
    // Velocity alignment: length added per unit of speed.
    public float Stretch { get; init; } = 0.02f;
}

public sealed record VfxTrailDefinition
{
    public string Texture { get; init; } = "";
    public VfxBlend Blend { get; init; } = VfxBlend.Additive;
    public float Intensity { get; init; } = 1f;
    public VfxAnchor Anchor { get; init; } = VfxAnchor.Caster;
    public string Bone { get; init; } = "";
    public float[] Offset { get; init; } = [0f, 0f, 0f];
    public float Width { get; init; } = 12f;
    // Seconds a trail point lives.
    public float Lifetime { get; init; } = 0.25f;
    public float Delay { get; init; }
    public float EmitTime { get; init; }
    // Colour along the trail from head (0) to tail (1).
    public VfxKey[] Color { get; init; } = [new(0f, [1f, 1f, 1f, 1f]), new(1f, [1f, 1f, 1f, 0f])];
}

public sealed record VfxLightDefinition
{
    public VfxAnchor Anchor { get; init; } = VfxAnchor.Point;
    public float[] Offset { get; init; } = [0f, 30f, 0f];
    public float[] Color { get; init; } = [1f, 1f, 1f];
    // Legacy light units: power sets the radius (power / 2, at least 64) and brightness, decay the fade per 1/70 s.
    public int Power { get; init; } = 160;
    public int Decay { get; init; } = 4;
    public float Delay { get; init; }
}

public sealed record VfxSoundDefinition
{
    // Path under the asset root, as the original sound tables give it (wav\Effects\...).
    public string Path { get; init; } = "";
    public float Delay { get; init; }
    public float Volume { get; init; } = 1f;
}

// An original particle script played by the legacy engine, attached like any other layer.
public sealed record VfxLegacyDefinition
{
    public string Script { get; init; } = "";
    public VfxAnchor Anchor { get; init; } = VfxAnchor.Point;
    public string Bone { get; init; } = "";
    public float[] Offset { get; init; } = [0f, 0f, 0f];
    public float Delay { get; init; }
    public bool Follow { get; init; }
}

public sealed record VfxDefinition
{
    public required string Name { get; init; }
    // Lifetime of the effect in seconds; layers stop emitting then and the effect ends when every particle has died.
    public float Duration { get; init; } = 1f;
    public VfxEmitterDefinition[] Emitters { get; init; } = [];
    public VfxTrailDefinition[] Trails { get; init; } = [];
    public VfxLightDefinition[] Lights { get; init; } = [];
    public VfxSoundDefinition[] Sounds { get; init; } = [];
    public VfxLegacyDefinition[] Legacy { get; init; } = [];

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };

    public static VfxDefinition[] ParseFile(string json)
    {
        var definitions = JsonSerializer.Deserialize<VfxDefinition[]>(json, Json) ?? [];
        foreach (var definition in definitions)
        {
            definition.Validate();
        }

        return definitions;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || !float.IsFinite(Duration) || Duration <= 0f || Duration > 60f)
        {
            throw new InvalidDataException($"Effect '{Name}' needs a name and a duration between 0 and 60 seconds.");
        }

        foreach (var emitter in Emitters)
        {
            if (emitter.MaxParticles is < 1 or > 4096 || emitter.Burst < 0 || emitter.Rate < 0f || emitter.Lifetime.Length != 2
                || emitter.Lifetime[0] <= 0f || emitter.Lifetime[1] < emitter.Lifetime[0] || emitter.Size.Length == 0 || emitter.Color.Length == 0
                || emitter.FrameColumns < 1 || emitter.FrameRows < 1 || !Vec3Ok(emitter.Offset) || !Vec3Ok(emitter.Direction)
                || !Vec3Ok(emitter.Gravity) || !RangeOk(emitter.Speed) || !RangeOk(emitter.Radius) || !RangeOk(emitter.Rotation)
                || !RangeOk(emitter.Spin) || !KeysOk(emitter.Size, 1) || !KeysOk(emitter.Color, 4))
            {
                throw new InvalidDataException($"Effect '{Name}' has an invalid emitter.");
            }
        }

        foreach (var trail in Trails)
        {
            if (trail.Width <= 0f || trail.Lifetime <= 0f || !Vec3Ok(trail.Offset) || !KeysOk(trail.Color, 4))
            {
                throw new InvalidDataException($"Effect '{Name}' has an invalid trail.");
            }
        }

        foreach (var light in Lights)
        {
            if (light.Power <= 0 || light.Decay <= 0 || light.Color.Length != 3 || !Vec3Ok(light.Offset))
            {
                throw new InvalidDataException($"Effect '{Name}' has an invalid light.");
            }
        }
    }

    private static bool Vec3Ok(float[] value) => value.Length == 3 && value.All(float.IsFinite);

    private static bool RangeOk(float[] value) => value.Length == 2 && value.All(float.IsFinite) && value[1] >= value[0];

    private static bool KeysOk(VfxKey[] keys, int width)
    {
        var previous = -1f;
        foreach (var key in keys)
        {
            if (key.V.Length != width || !float.IsFinite(key.T) || key.T < previous || key.T < 0f || key.T > 1f || !key.V.All(float.IsFinite))
            {
                return false;
            }

            previous = key.T;
        }

        return true;
    }

    internal static Vector3 Vec3(float[] value) => new(value[0], value[1], value[2]);
}
