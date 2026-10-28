using Microsoft.Xna.Framework;
using PristonTale.Contracts.Units;
using PristonTale.Effects.Vfx;

namespace PristonTale.Gameplay.Effects;

// Effects of used items built on the effect system: the skill-learn light of hair tints and force orbs, the weapon glow
// of a running force orb, the potion drops and the firecracker's launch sparks and flash.
public static class ItemVfx
{
    public const string Learn = "vfx.item.learn";
    public const string FirecrackerRocket = "vfx.item.firecracker.rocket";
    public const string FirecrackerFlash = "vfx.item.firecracker.flash";
    public const float GlowSeconds = 30f;
    private const string LearnTexture = "Effect/ImageData/SkillUp1/SkillUp1.tga";
    private const string Flare = "Effect/AssaEffect/flare.tga";

    // The orb colours by orb number (1..12).
    public static readonly Color[] OrbColors =
    [
        new(80, 20, 150), new(80, 20, 150), new(80, 20, 150), new(80, 20, 150), new(140, 90, 20), new(20, 100, 20), new(0, 100, 200),
        new(190, 30, 150), new(255, 40, 20), new(220, 255, 10), new(10, 255, 10), new(255, 255, 255)
    ];

    public static string OrbGlow(int orbIndex) => $"vfx.item.forceorb.{orbIndex}";

    // The drops of a potion by resource: 0 life (red), 1 mana (blue), 2 stamina (green).
    public static string Potion(int resource) => $"vfx.item.potion.{resource}";

    public static void Register(VfxLibrary library)
    {
        if (library is null)
        {
            return;
        }

        library.Register(LearnDefinition());
        library.Register(FirecrackerRocketDefinition());
        library.Register(FirecrackerFlashDefinition());
        for (var resource = 0; resource < 3; resource++)
        {
            library.Register(PotionDefinition(resource));
        }

        for (var index = 0; index < OrbColors.Length; index++)
        {
            library.Register(OrbDefinition(index));
        }
    }

    // The skill-learn light: thin streaks rise from a ring around the player, 60 short ones from 11 units out and 50
    // taller ones from 13 units out that climb higher and later, with a white light.
    private static VfxDefinition LearnDefinition() => new()
    {
        Name = Learn,
        Duration = 0.75f,
        Emitters =
        [
            Streaks(60, 0.3f, [11f, 15.5f], [0.55f, 0.65f], [9f, 11f], 2.2f),
            Streaks(50, 0.7f, [13f, 15.5f], [0.9f, 1.05f], [18f, 22f], 1.6f) with { Delay = 0.2f }
        ],
        Lights = [new VfxLightDefinition { Anchor = VfxAnchor.Caster, Offset = [0f, 20f, 0f], Color = [0.78f, 0.78f, 0.78f], Power = 260, Decay = 1 }]
    };

    private static VfxEmitterDefinition Streaks(int count, float emitSeconds, float[] radius, float[] lifetime, float[] speed, float width) => new()
    {
        Texture = LearnTexture,
        Anchor = VfxAnchor.Caster,
        Shape = VfxShape.Ring,
        Radius = radius,
        EmitTime = emitSeconds,
        Rate = count / emitSeconds,
        MaxParticles = count,
        Lifetime = lifetime,
        Speed = speed,
        Direction = [0f, 1f, 0f],
        Spread = 2f,
        Align = VfxAlign.Velocity,
        Stretch = 0.45f,
        Softness = 2f,
        Intensity = 2f,
        Size = [new VfxKey(0f, [width])],
        Color = [new VfxKey(0f, [1f, 1f, 1f, 0f]), new VfxKey(0.3f, [1f, 1f, 1f, 0.4f]), new VfxKey(0.6f, [1f, 1f, 1f, 0.7f]),
            new VfxKey(1f, [1f, 1f, 1f, 0f])]
    };

    // A potion: the Light1 halo, and 30 drops that leave in random directions at 70 fixed units a tick outward and up,
    // fall at 3 to 5 units a tick squared and turn a third of a turn plus 500 angle units about the drinker while they
    // move (55 to 74 ticks), then settle and fade through their animation's last frame. Sizes are full extents.
    private static VfxDefinition PotionDefinition(int resource)
    {
        const float speed = 70f / GameUnits.FixedScale * GameUnits.TicksPerSecond;
        const float gravityPerUnit = GameUnits.TicksPerSecond * GameUnits.TicksPerSecond / GameUnits.FixedScale;
        const float firstTick = 55f * GameUnits.TickSeconds;
        const float lastTick = 74f * GameUnits.TickSeconds;
        const float turn = (GameUnits.AngleTurn / 3 + 500) * 360f / GameUnits.AngleTurn;
        return new VfxDefinition
        {
            Name = Potion(resource),
            Duration = 1.1f,
            Parts =
            [
                new VfxPartDefinition { Animation = "Light1.ini", Size = [120f, 120f] },
                new VfxPartDefinition
                {
                    Count = 30,
                    Animation = $"Potion{resource + 1}.ini",
                    Loop = true,
                    Size = [8f, 8f],
                    RingRandom = true,
                    Radial = [speed, speed],
                    Rise = [speed, speed],
                    Gravity = [-5f * gravityPerUnit, -3f * gravityPerUnit],
                    MoveTime = [firstTick, lastTick],
                    Swirl = [turn / lastTick, turn / firstTick],
                    End = VfxPartEnd.Hold
                }
            ]
        };
    }

    // The 24 sparks a firecracker rocket throws where it lifts off: FireParticle1 billboards bursting in every direction
    // at 20 to 45 units a second and falling at 60.
    private static VfxDefinition FirecrackerRocketDefinition() => new()
    {
        Name = FirecrackerRocket,
        Duration = 1f,
        Parts = [new VfxPartDefinition { Count = 24, Animation = "FireParticle1.ini", Size = [10f, 10f], Burst = [20f, 45f], Gravity = [-60f, -60f] }]
    };

    // The flash where the rocket bursts.
    private static VfxDefinition FirecrackerFlashDefinition() => new()
    {
        Name = FirecrackerFlash,
        Duration = 0.5f,
        Parts = [new VfxPartDefinition { Animation = "Power1.ini", Size = [100f, 100f] }]
    };

    private static VfxDefinition OrbDefinition(int index)
    {
        var color = OrbColors[index].ToVector3();
        return new VfxDefinition
        {
            Name = OrbGlow(index),
            Duration = GlowSeconds,
            Emitters =
            [
                new VfxEmitterDefinition
                {
                    Texture = Flare,
                    Anchor = VfxAnchor.Caster,
                    Bone = "Bip weapon01",
                    Local = true,
                    Rate = 24f,
                    MaxParticles = 48,
                    Lifetime = [0.35f, 0.5f],
                    Shape = VfxShape.Sphere,
                    Radius = [0f, 10f],
                    Softness = 0f,
                    Intensity = 2f,
                    Size = [new VfxKey(0f, [5f]), new VfxKey(0.5f, [9f]), new VfxKey(1f, [5f])],
                    Color = [new VfxKey(0f, [color.X, color.Y, color.Z, 0f]), new VfxKey(0.3f, [color.X, color.Y, color.Z, 0.8f]),
                        new VfxKey(1f, [color.X, color.Y, color.Z, 0f])]
                }
            ]
        };
    }
}
