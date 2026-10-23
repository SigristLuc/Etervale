using Microsoft.Xna.Framework;
using PristonTale.Effects;
using PristonTale.Effects.Effects;
using PristonTale.Effects.Particles;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Gameplay.Effects;

// What other players and the user see when an item is used, with the original particle scripts and billboards:
// firecrackers fire four rockets half a second apart, each bursting 150 units up into a scripted shape chosen from the
// use's roll; hair tints and force orbs raise the skill-learn light ring; skill masters play the agony glow.
public sealed class LegacyItemEffects
{
    public const string LearnSound = "wav/Effects/Skill/Learn_Skill.wav";
    public const string DrinkSound = "Image/SinImage/Sound/drink2.wav";
    public static readonly string[] FirecrackerSounds = ["wav/Effects/event/firecracker01.wav", "wav/Effects/event/firecracker02.wav",
        "wav/Effects/event/firecracker03.wav"];
    private static readonly string[] Crackers = ["FireCrackerAll", "FireCrackerHeart", "FireCrackerMoon", "FireCrackerStar", "FireCrackerNormal"];
    private static readonly string[] Bombs = ["FireCrackerBombHeart", "FireCrackerBombMoon", "FireCrackerBombStar"];
    private const float BurstHeight = 100f;
    private const int MaxInstances = 128;

    private readonly EffectsEngine _engine;
    private readonly PristonTransientLights _lights;
    private readonly SkillSoundBank _sounds;
    private readonly Dictionary<string, EffectDefinition> _scripts = new(StringComparer.Ordinal);
    private readonly EffectDefinition _rocket;
    private readonly EffectDefinition _flash;
    private readonly EffectDefinition _learn;
    private readonly List<Burst> _bursts = new();
    private readonly Random _random = new();

    public LegacyItemEffects(LegacyEffectLibrary library, string assetRoot, EffectsEngine engine, PristonTransientLights lights, SkillSoundBank sounds)
    {
        _engine = engine;
        _lights = lights;
        _sounds = sounds;
        foreach (var name in Crackers.Concat(Bombs))
        {
            if (library.ParticleScript(assetRoot, name) is { } script)
            {
                _scripts[name] = script;
            }
        }

        _rocket = Billboards(library.Animation(assetRoot, "FireParticle1.ini"), "FirecrackerRocket", 24, 5f, new FloatRange(20f, 45f), MathHelper.Pi,
            new Vector3(0f, -60f, 0f));
        _flash = Billboards(library.Animation(assetRoot, "Power1.ini"), "FirecrackerFlash", 1, 50f, FloatRange.Constant(0f), 0f, Vector3.Zero);
        _learn = Learn(library.Animation(assetRoot, "SkillUp2.ini"), library.Animation(assetRoot, "SkillUp1.ini"));
        sounds?.Preload(FirecrackerSounds.Append(LearnSound).Append(DrinkSound));
    }

    public int PendingBursts => _bursts.Count;

    // The original firecracker table: a roll of 0..16382 names four bursts, one per byte, read high byte first.
    public static int[] FirecrackerBursts(int roll) => [roll / 8000, roll / 400 % 20, roll / 20 % 20, roll % 20];

    // A burst of 0..19 picks the shape (burst / 4) and the bomb (burst % 4, 3 for none).
    public static (string Cracker, string Bomb) Shapes(int burst) =>
        burst is >= 0 and < 20 ? (Crackers[burst / 4], burst % 4 < 3 ? Bombs[burst % 4] : null) : (null, null);

    public void Play(ItemEffectKind effect, uint entity, int variant, Vector3 position, Vector3 listener, double nowMs)
    {
        switch (effect)
        {
            case ItemEffectKind.Firecracker:
                var bursts = FirecrackerBursts(variant);
                for (var index = 0; index < bursts.Length; index++)
                {
                    _bursts.Add(new Burst(entity, nowMs + 500d * index, bursts[index]));
                }

                break;
            case ItemEffectKind.Learn:
            case ItemEffectKind.ForceOrb:
                Spawn(_learn, position);
                _lights?.Add(position, new Color(200, 200, 200), 260, 1);
                _sounds?.Play(LearnSound, position, listener);
                break;
            case ItemEffectKind.Mastery:
                _sounds?.Play(DrinkSound, position, listener);
                break;
        }
    }

    // Fires the bursts that came due; position resolves the entity's drawn position (null when it left the view).
    public void Update(double nowMs, Func<uint, Vector3?> position, Vector3 listener)
    {
        for (var index = _bursts.Count - 1; index >= 0; index--)
        {
            var burst = _bursts[index];
            if (burst.DueMs > nowMs)
            {
                continue;
            }

            _bursts.RemoveAt(index);
            if (position(burst.Entity) is not { } feet)
            {
                continue;
            }

            var start = feet + new Vector3(_random.Next(-20, 21), _random.Next(-5, 6) + 50, _random.Next(-20, 21));
            var top = start + Vector3.Up * BurstHeight;
            Spawn(_rocket, start);
            Spawn(_flash, top);
            var (cracker, bomb) = Shapes(burst.Shape);
            if (cracker is not null && _scripts.TryGetValue(cracker, out var shape))
            {
                Spawn(shape, top);
            }

            if (bomb is not null && _scripts.TryGetValue(bomb, out var bombShape))
            {
                Spawn(bombShape, top);
            }

            _sounds?.Play(FirecrackerSounds[_random.Next(FirecrackerSounds.Length)], top, listener);
        }
    }

    private void Spawn(EffectDefinition definition, Vector3 position)
    {
        if (definition is not null && _engine.Instances.Count < MaxInstances)
        {
            _engine.Spawn(definition, position);
        }
    }

    private static EffectDefinition Billboards(LegacyBillboardAnimation animation, string name, int count, float size, FloatRange speed, float spread,
        Vector3 gravity)
    {
        if (animation is null)
        {
            return null;
        }

        return new EffectDefinition
        {
            Name = name,
            Duration = 0.001f,
            Emitters =
            [
                new EmitterDefinition
                {
                    MaxParticles = count,
                    BurstCount = count,
                    Frames = animation.Textures,
                    Animation = animation.Animation,
                    Lifetime = FloatRange.Constant(animation.Animation.Duration),
                    InitialSpeed = speed,
                    DirectionSpreadRadians = spread,
                    Gravity = gravity,
                    Size = SizeOverLife.Constant(size)
                }
            ]
        };
    }

    // The skill-learn ring: light streaks rise from a ring of twelve points around the player.
    private static EffectDefinition Learn(LegacyBillboardAnimation inner, LegacyBillboardAnimation outer)
    {
        if (inner is null || outer is null)
        {
            return null;
        }

        const int Points = 12;
        var emitters = new List<EmitterDefinition>();
        var offsets = new List<Vector3>();
        for (var point = 0; point < Points; point++)
        {
            var angle = point * MathHelper.TwoPi / Points;
            var direction = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
            emitters.Add(Streaks(inner, 5, 6f, new FloatRange(8f, 12f)));
            offsets.Add(direction * 11f);
            emitters.Add(Streaks(outer, 4, 4f, new FloatRange(18f, 28f)));
            offsets.Add(direction * 13f);
        }

        return new EffectDefinition { Name = "ItemLearn", Duration = 0.7f, Emitters = emitters, EmitterOffsets = offsets };
    }

    private static EmitterDefinition Streaks(LegacyBillboardAnimation animation, int count, float size, FloatRange speed) => new()
    {
        MaxParticles = count,
        EmissionRate = count / 0.7f,
        Frames = animation.Textures,
        Animation = animation.Animation,
        Lifetime = FloatRange.Constant(animation.Animation.Duration),
        InitialSpeed = speed,
        EmitDirection = Vector3.Up,
        DirectionSpreadRadians = 0.15f,
        Size = SizeOverLife.Constant(size)
    };

    private readonly record struct Burst(uint Entity, double DueMs, int Shape);
}
