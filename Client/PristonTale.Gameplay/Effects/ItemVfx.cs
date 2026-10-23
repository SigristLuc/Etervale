using Microsoft.Xna.Framework;
using PristonTale.Effects.Vfx;

namespace PristonTale.Gameplay.Effects;

// Effects of used items built on the effect system: the skill-learn light of hair tints and force orbs, and the weapon
// glow of a running force orb.
public static class ItemVfx
{
    public const string Learn = "vfx.item.learn";
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

    public static void Register(VfxLibrary library)
    {
        if (library is null)
        {
            return;
        }

        library.Register(LearnDefinition());
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
