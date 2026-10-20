using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering.Post;

namespace PristonTale.Engine3D.Rendering.Lighting;

// Sunlight scattered by a height fog, shadowed by the cascades (Shaders/Passes/Volumetric.fx): light shafts through
// gaps and a warm glow toward a low sun, marched at quarter resolution and added to the scene.
internal sealed class PristonVolumetricLight : IDisposable
{
    public const string Asset = "Effects/Volumetric";
    // Fog density per world unit at the reference height (a little above the ground around the camera focus), its scale
    // height, the longest march and the forward-scattering anisotropy.
    public const float Density = 1.2e-4f;
    public const float ScaleHeight = 600f;
    public const float ReferenceBelowFocus = 50f;
    public const float MaxDistance = 6000f;
    public const float Anisotropy = 0.6f;

    private static readonly BlendState Add = new()
    {
        Name = "PristonVolumetricAdd",
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One
    };

    private readonly GraphicsDevice _device;
    private readonly PristonFullscreenTriangle _triangle;
    private readonly Effect _effect;
    private readonly EffectTechnique _march;
    private readonly EffectTechnique _composite;
    private readonly EffectParameterCollection _parameters;
    private readonly Matrix[] _cascadeMatrices = new Matrix[PristonShadowCascades.MaxCascades];
    private readonly Vector4[] _cascadeAtlas = new Vector4[PristonShadowCascades.MaxCascades];
    private RenderTarget2D _inscatter;

    public PristonVolumetricLight(GraphicsDevice device, PristonFullscreenTriangle triangle, Effect effect)
    {
        _device = device;
        _triangle = triangle;
        _effect = effect;
        _march = effect.Techniques["March"];
        _composite = effect.Techniques["Composite"];
        _parameters = effect.Parameters;
    }

    public void Run(Texture2D depth, Texture2D shadowAtlas, PristonShadowCascades cascades, Matrix view, Matrix projection,
        Vector3 cameraPosition, Vector3 focus, Vector3 towardSun, Vector3 sunRadiance, float noiseOffset, Action bindScene)
    {
        var width = Math.Max(depth.Width / 4, 1);
        var height = Math.Max(depth.Height / 4, 1);
        EnsureSize(width, height);
        for (var c = 0; c < PristonShadowCascades.MaxCascades; c++)
        {
            var source = Math.Min(c, cascades.Count - 1);
            _cascadeMatrices[c] = cascades.ViewProjections[source];
            _cascadeAtlas[c] = cascades.AtlasRects[source];
        }

        var splits = new Vector4(
            cascades.Splits[0],
            cascades.Splits[Math.Min(1, cascades.Count - 1)],
            cascades.Splits[Math.Min(2, cascades.Count - 1)],
            cascades.Splits[cascades.Count - 1]);
        _parameters["DepthMap"].SetValue(depth);
        _parameters["ShadowMap"].SetValue(shadowAtlas);
        _parameters["VolumeSize"].SetValue(new Vector4(width, height, depth.Width, depth.Height));
        _parameters["VolumeProjection"].SetValue(new Vector4(1f / projection.M11, 1f / projection.M22, MaxDistance, noiseOffset));
        _parameters["CameraRight"].SetValue(new Vector3(view.M11, view.M21, view.M31));
        _parameters["CameraUp"].SetValue(new Vector3(view.M12, view.M22, view.M32));
        _parameters["CameraForward"].SetValue(-new Vector3(view.M13, view.M23, view.M33));
        _parameters["CameraPosition"].SetValue(cameraPosition);
        _parameters["FogDensity"].SetValue(Density);
        _parameters["FogFalloff"].SetValue(1f / ScaleHeight);
        _parameters["FogReferenceHeight"].SetValue(focus.Y - ReferenceBelowFocus);
        _parameters["PhaseG"].SetValue(Anisotropy);
        _parameters["SunDirection"].SetValue(towardSun);
        _parameters["SunRadiance"].SetValue(sunRadiance);
        _parameters["CascadeCount"].SetValue((float)cascades.Count);
        _parameters["CascadeViewProjection"].SetValue(_cascadeMatrices);
        _parameters["CascadeAtlas"].SetValue(_cascadeAtlas);
        _parameters["CascadeSplits"].SetValue(splits);

        _device.SetRenderTarget(_inscatter);
        _device.BlendState = BlendState.Opaque;
        _effect.CurrentTechnique = _march;
        _triangle.Draw(_effect);

        bindScene();
        _parameters["Inscatter"].SetValue(_inscatter);
        _device.BlendState = Add;
        _effect.CurrentTechnique = _composite;
        _triangle.Draw(_effect);
        _device.BlendState = BlendState.Opaque;
    }

    private void EnsureSize(int width, int height)
    {
        if (_inscatter is not null && _inscatter.Width == width && _inscatter.Height == height)
        {
            return;
        }

        _inscatter?.Dispose();
        _inscatter = new RenderTarget2D(_device, width, height, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0,
            RenderTargetUsage.PreserveContents);
    }

    public void Dispose()
    {
        _inscatter?.Dispose();
        _effect.Dispose();
    }
}
