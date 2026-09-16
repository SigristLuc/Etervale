using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

internal sealed class PristonMaterialRuntimeFactory
{
    private readonly PristonTextureLoader _textureLoader;

    public PristonMaterialRuntimeFactory(PristonTextureLoader textureLoader)
    {
        _textureLoader = textureLoader;
    }

    public PristonMaterialRuntime Create(string assetDirectory, PristonMaterial material, bool softAlpha = false)
    {
        var alphaTest = !softAlpha && UsesAlphaTest(material);

        // BLEND_ALPHA(1) with zero transparency is opaque in the reference and remains mergeable.
        var lateBlend = material.BlendType is 2 or 3 or 4 or 5 or 6;
        var transparent = material.RenderLast || (!(alphaTest && !lateBlend) && (lateBlend || material.Transparency > 0.001f));
        var (windX, windZ) = WindAmplitudes(material.SurfaceFlags);
        var formState = FirstTextureFormState(material);
        var (secondTexture, secondFormState) = LoadSecondTexture(assetDirectory, material);
        // MULTIMIX slots carry independent scroll states.
        if (secondTexture != null)
            formState = material.TextureFormStates.Count > 0 ? material.TextureFormStates[0] : 0u;
        return new PristonMaterialRuntime(
            LoadMaterialTextures(assetDirectory, material),
            material.TwoSided,
            transparent,
            alphaTest,
            material.BlendType,
            material.Transparency,
            formState,
            material.FrameMask,
            material.ShiftFrameSpeed,
            material.AnimationFrame,
            material.IsSelfIllum,
            material.IsInvisible,
            material.IsWater,
            material.IsBlinking,
            windX,
            windZ,
            formState == FsReflex,
            secondTexture,
            secondFormState);
    }

    private (Texture2D texture, uint formState) LoadSecondTexture(string assetDirectory, PristonMaterial material)
    {
        if (!material.HasSecondTexture)
            return (null, 0u);

        var texture = _textureLoader.LoadTexture(assetDirectory, material.SecondTextureName, material.SecondOpacityName);
        var formState = material.TextureFormStates.Count > 1 ? material.TextureFormStates[1] : 0u;
        return (texture, formState);
    }

    private const uint FsReflex = 5;

    // Bare Move is WINDZ1 in the original rather than a UV-scroll flag.
    private const float WindSmall = 8f;
    private const float WindLarge = 28f;

    private static (float x, float z) WindAmplitudes(PristonMaterialSurfaceFlags sf)
    {
        var x = (sf & PristonMaterialSurfaceFlags.MoveX2) != 0 ? WindLarge
              : (sf & PristonMaterialSurfaceFlags.MoveX1) != 0 ? WindSmall : 0f;
        var z = (sf & PristonMaterialSurfaceFlags.MoveZ2) != 0 ? WindLarge
              : (sf & (PristonMaterialSurfaceFlags.MoveZ1 | PristonMaterialSurfaceFlags.Move)) != 0 ? WindSmall : 0f;
        return (x, z);
    }

    private Texture2D[] LoadMaterialTextures(string assetDirectory, PristonMaterial material)
    {
        if (material.HasAnimatedTexture)
        {
            var textures = new Texture2D[material.AnimationTextureNames.Count];
            for (var i = 0; i < textures.Length; i++)
                textures[i] = _textureLoader.LoadTexture(assetDirectory, material.AnimationTextureNames[i], string.Empty);
            return textures;
        }

        return [_textureLoader.LoadTexture(assetDirectory, material.TextureName, material.OpacityName)];
    }

    private static bool UsesAlphaTest(PristonMaterial material)
    {
        if (material.OpacityName.Length > 0 || IsTga(material.TextureName))
            return true;

        for (var i = 0; i < material.AnimationTextureNames.Count; i++)
            if (IsTga(material.AnimationTextureNames[i]))
                return true;

        return false;
    }

    private static bool IsTga(string textureName) =>
        textureName.EndsWith(".tga", StringComparison.OrdinalIgnoreCase);

    private static uint FirstTextureFormState(PristonMaterial material)
    {
        for (var i = 0; i < material.TextureFormStates.Count; i++)
            if (material.TextureFormStates[i] != 0)
                return material.TextureFormStates[i];

        return 0;
    }
}
