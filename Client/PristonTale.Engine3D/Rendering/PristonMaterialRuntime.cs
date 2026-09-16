using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

internal sealed class PristonMaterialRuntime
{
    public PristonMaterialRuntime(
        Texture2D[] textures,
        bool twoSided,
        bool transparent,
        bool alphaTest,
        uint blendType,
        float transparency,
        uint textureFormState,
        uint frameMask,
        uint shiftFrameSpeed,
        uint animationFrame,
        bool selfIllum,
        bool invisible,
        bool water,
        bool blinking,
        float windAmplitudeX,
        float windAmplitudeZ,
        bool reflex,
        Texture2D secondTexture,
        uint secondTextureFormState)
    {
        Textures = textures;
        SecondTexture = secondTexture;
        SecondTextureFormState = secondTextureFormState;
        TwoSided = twoSided;
        Transparent = transparent;
        AlphaTest = alphaTest;
        BlendType = blendType;
        Transparency = transparency;
        TextureFormState = textureFormState;
        FrameMask = frameMask;
        ShiftFrameSpeed = shiftFrameSpeed;
        AnimationFrame = animationFrame;
        SelfIllum = selfIllum;
        Invisible = invisible;
        Water = water;
        Blinking = blinking;
        WindAmplitudeX = windAmplitudeX;
        WindAmplitudeZ = windAmplitudeZ;
        Reflex = reflex;
        var textureHash = textures.Length == 0 ? 0 : textures[0].GetHashCode() & 0xFFFF;
        SortKey = ((ulong)(alphaTest ? 1u : 0u) << 48)
            | ((ulong)(transparent ? 1u : 0u) << 47)
            | ((ulong)(twoSided ? 1u : 0u) << 46)
            | ((ulong)(blendType & 0xFFFF) << 16)
            | (uint)textureHash;
    }

    public Texture2D[] Textures { get; }
    public bool TwoSided { get; }
    public bool Transparent { get; }

    // MULTIMIX combines slot 1 additively over slot 0.
    public Texture2D SecondTexture { get; }
    public uint SecondTextureFormState { get; }
    public bool HasSecondTexture => SecondTexture != null;

    public bool Invisible { get; }

    public bool Water { get; }

    public bool Blinking { get; }

    public float WindAmplitudeX { get; }
    public float WindAmplitudeZ { get; }
    public bool Wind => WindAmplitudeX != 0f || WindAmplitudeZ != 0f;

    public bool Reflex { get; }

    public bool AlphaTest { get; }
    public uint BlendType { get; }
    public float Transparency { get; }
    public float AlphaCutoff => 60f / 255f;

    // Global transparency at/below which a surface counts as visually solid (writes depth /
    // casts a shadow). Mirrors the reference depth-write threshold (RenderMesh.cpp:285).
    private const float SolidTransparencyThreshold = 0.2f;

    // BLEND_ALPHA(1) is not a late blend in the reference.
    private bool IsLateBlend => BlendType is 2 or 3 or 4 or 5 or 6;

    public bool CastsShadow =>
        !IsLateBlend && (AlphaTest || Transparency <= SolidTransparencyThreshold);

    public bool ReceivesShadow => true;
    public bool SelfIllum { get; }
    public uint TextureFormState { get; }
    public uint FrameMask { get; }
    public uint ShiftFrameSpeed { get; }
    public uint AnimationFrame { get; }
    public BlendState BlendState
    {
        get
        {
            if (AlphaTest && Transparency <= SolidTransparencyThreshold)
                return BlendState.Opaque;

            // These values follow the reference SMMAT_BLEND table; several modes are not alpha blends.
            return BlendType switch
            {
                1 => BlendState.NonPremultiplied, // ALPHA:     SrcAlpha, InvSrcAlpha
                2 => BlendColor,                  // COLOR:     SrcColor, InvSrcColor
                3 => BlendShadow,                 // SHADOW:    Zero, SrcColor      (darken)
                4 => BlendState.Additive,         // LAMP:      SrcAlpha, One       (== MG Additive)
                5 => BlendAddColor,               // ADDCOLOR:  SrcColor, One       (additive)
                6 => BlendInvShadow,              // INVSHADOW: Zero, InvSrcColor
                _ => Transparent ? BlendState.NonPremultiplied : BlendState.Opaque,
            };
        }
    }

    // The alpha channel must remain One/InverseSourceAlpha for offscreen composition.
    private static readonly BlendState BlendColor = MakeBlend("PristonBlendColor", Blend.SourceColor, Blend.InverseSourceColor);
    private static readonly BlendState BlendShadow = MakeBlend("PristonBlendShadow", Blend.Zero, Blend.SourceColor);
    private static readonly BlendState BlendAddColor = MakeBlend("PristonBlendAddColor", Blend.SourceColor, Blend.One);
    private static readonly BlendState BlendInvShadow = MakeBlend("PristonBlendInvShadow", Blend.Zero, Blend.InverseSourceColor);

    private static BlendState MakeBlend(string name, Blend colorSource, Blend colorDestination) => new()
    {
        Name = name,
        ColorSourceBlend = colorSource,
        ColorDestinationBlend = colorDestination,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha,
        ColorBlendFunction = BlendFunction.Add,
        AlphaBlendFunction = BlendFunction.Add,
    };

    public ulong SortKey { get; }

    public Texture2D SelectTexture(uint renderTime)
    {
        if (Textures.Length == 1)
            return Textures[0];

        uint frame;
        if (AnimationFrame == 0x100)
        {
            var shift = (int)Math.Min(ShiftFrameSpeed, 31);
            frame = (renderTime >> shift) & FrameMask;
        }
        else
        {
            frame = AnimationFrame;
        }

        return Textures[(int)(frame % Textures.Length)];
    }
}
