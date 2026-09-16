using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Engine3D.Rendering;

internal sealed class PristonEffectPipeline
{
    private const float ShadowMapTexelSize = 1.0f / 8192.0f; // must match PristonRenderer.ShadowMapSize
    private const float StaticShadowNormalOffset = 0f;
    private const float SkinnedShadowNormalOffset = 1.25f;

    // The skinning VS uses three float4 constants per affine bone (192 for 64 bones),
    // plus View/Projection and reflection uniforms, within the vs_3_0 limit of 256.
    // Pixel lighting constants belong to a separate shader stage/register budget.
    internal const int MaxBones = 64;

    private readonly EffectBinding _opaque;
    private readonly EffectBinding _alphaTest;
    private readonly EffectBinding _depthOnly;
    private readonly EffectBinding _depthAlphaTest;
    private readonly SkinnedEffectBinding _skinnedOpaque;
    private readonly SkinnedEffectBinding _skinnedAlphaTest;
    private readonly SkinnedEffectBinding _skinnedDepthOnly;
    private readonly SkinnedEffectBinding _skinnedDepthAlphaTest;
    private readonly Vector4[] _pointLightPositions = new Vector4[16];
    private readonly Vector4[] _pointLightColors = new Vector4[16];

    public PristonEffectPipeline(ContentManager content, IClientLog log)
    {
        _opaque = new EffectBinding(LoadRequired(content, "Effects/PristonOpaque", log));
        _alphaTest = new EffectBinding(LoadRequired(content, "Effects/PristonAlphaTest", log));
        _depthOnly = new EffectBinding(LoadRequired(content, "Effects/PristonDepthOnly", log));
        _depthAlphaTest = new EffectBinding(LoadRequired(content, "Effects/PristonDepthAlphaTest", log));
        _skinnedOpaque = new SkinnedEffectBinding(LoadRequired(content, "Effects/PristonSkinnedOpaque", log));
        _skinnedAlphaTest = new SkinnedEffectBinding(LoadRequired(content, "Effects/PristonSkinnedAlphaTest", log));
        _skinnedDepthOnly = new SkinnedEffectBinding(LoadRequired(content, "Effects/PristonSkinnedDepthOnly", log));
        _skinnedDepthAlphaTest = new SkinnedEffectBinding(LoadRequired(content, "Effects/PristonSkinnedDepthAlphaTest", log));

        var texelSize = new Vector2(ShadowMapTexelSize, ShadowMapTexelSize);
        _opaque.SetConstantParameters(texelSize, StaticShadowNormalOffset);
        _alphaTest.SetConstantParameters(texelSize, StaticShadowNormalOffset);
        _depthOnly.SetConstantParameters(texelSize, StaticShadowNormalOffset);
        _depthAlphaTest.SetConstantParameters(texelSize, StaticShadowNormalOffset);

        _skinnedOpaque.SetConstantParameters(texelSize, SkinnedShadowNormalOffset);
        _skinnedAlphaTest.SetConstantParameters(texelSize, SkinnedShadowNormalOffset);
        _skinnedDepthOnly.SetConstantParameters(texelSize, SkinnedShadowNormalOffset);
        _skinnedDepthAlphaTest.SetConstantParameters(texelSize, SkinnedShadowNormalOffset);
    }

    private readonly List<PristonPointLight> _staticPointLights = new(8);

    public void ApplyFrame(PristonFrameContext frame)
    {
        PackPointLights(frame.Lighting);
        // Static geometry receives only temporary skill lights; its permanent lamps remain baked.
        var staticFrame = frame with { Lighting = frame.Lighting with { PointLights = _staticPointLights } };
        _staticPointLights.Clear();
        for (int index = 0; index < frame.Lighting.TransientLightCount; index++)
        {
            _staticPointLights.Add(frame.Lighting.PointLights[index]);
        }

        _opaque.ApplyFrame(staticFrame, _pointLightPositions, _pointLightColors);
        _alphaTest.ApplyFrame(staticFrame, _pointLightPositions, _pointLightColors);
        _depthOnly.ApplyFrame(frame);
        _depthAlphaTest.ApplyFrame(frame);

        _skinnedOpaque.ApplyFrame(frame, _pointLightPositions, _pointLightColors);
        _skinnedAlphaTest.ApplyFrame(frame, _pointLightPositions, _pointLightColors);
        _skinnedDepthOnly.ApplyFrame(frame);
        _skinnedDepthAlphaTest.ApplyFrame(frame);
    }

    private void PackPointLights(PristonLighting lighting)
    {
        Array.Clear(_pointLightPositions);
        Array.Clear(_pointLightColors);
        var count = Math.Min(lighting.PointLights.Count, _pointLightPositions.Length);
        for (var i = 0; i < count; i++)
        {
            var light = lighting.PointLights[i];
            _pointLightPositions[i] = new Vector4(light.Position, light.Radius);
            _pointLightColors[i] = new Vector4(light.Color, light.Intensity);
        }
    }

    public void ApplyLampShadows(LampShadowSystem lamps)
    {
        _opaque.SetLampShadows(lamps);
        _alphaTest.SetLampShadows(lamps);
    }

    public void ApplyDepthFrame(Matrix view, Matrix projection, uint renderTimeMs)
    {
        _depthOnly.ApplyMatrices(Matrix.Identity, view, projection);
        _depthAlphaTest.ApplyMatrices(Matrix.Identity, view, projection);
        // Depth and color passes must use the same vertex-animation clock.
        _depthOnly.SetVertexAnimTime(renderTimeMs);
        _depthAlphaTest.SetVertexAnimTime(renderTimeMs);
        _skinnedDepthOnly.ApplyViewProjection(view, projection);
        _skinnedDepthAlphaTest.ApplyViewProjection(view, projection);
    }

    public Effect SelectEffect(PristonMaterialRuntime material, Texture2D texture, Vector2 uvScrollOffset, Vector4 windParams, Vector2 uvScrollOffset2, float opacity = 1f)
    {
        var binding = material.AlphaTest ? _alphaTest : _opaque;
        binding.ApplyMaterial(texture, material, uvScrollOffset, windParams, uvScrollOffset2, opacity);
        return binding.Effect;
    }

    public Effect SelectOpaqueDepthEffect(PristonMaterialRuntime material, Vector4 windParams)
    {
        _depthOnly.ApplyDepthVertexAnimation(material.Water, windParams);
        return _depthOnly.Effect;
    }

    public Effect SelectAlphaTestDepthEffect(PristonMaterialRuntime material, Texture2D texture, Vector4 windParams, Vector2 uvScrollOffset)
    {
        _depthAlphaTest.ApplyDepthAlphaTestMaterial(texture, uvScrollOffset, material.Water, windParams);
        return _depthAlphaTest.Effect;
    }

    public Effect SelectSkinnedEffect(
        PristonMaterialRuntime material,
        Texture2D texture,
        Vector2 uvScrollOffset,
        PristonSkinPalette palette,
        Vector3 actorColorAdd)
    {
        var binding = material.AlphaTest ? _skinnedAlphaTest : _skinnedOpaque;
        binding.SetBonePalette(palette);
        binding.SetActorColorAdd(actorColorAdd);
        binding.ApplyMaterial(texture, material, uvScrollOffset);
        return binding.Effect;
    }

    public Effect SelectSkinnedOpaqueDepthEffect(PristonSkinPalette palette)
    {
        _skinnedDepthOnly.SetBonePalette(palette);
        return _skinnedDepthOnly.Effect;
    }

    public Effect SelectSkinnedAlphaTestDepthEffect(Texture2D texture, PristonSkinPalette palette, Vector2 uvScrollOffset)
    {
        _skinnedDepthAlphaTest.SetBonePalette(palette);
        _skinnedDepthAlphaTest.ApplyDepthAlphaTestMaterial(texture, uvScrollOffset, water: false, Vector4.Zero);
        return _skinnedDepthAlphaTest.Effect;
    }

    internal static Effect LoadRequired(ContentManager content, string assetName, IClientLog log)
    {
        Effect effect;
        try
        {
            effect = content.Load<Effect>(assetName);
        }
        catch (ContentLoadException exception)
        {
            var message = $"Required shader '{assetName}' could not be loaded from '{content.RootDirectory}'. Rebuild and deploy the client Content directory.";
            log.Error(message, exception);
            throw new ContentLoadException(message, exception);
        }

        if (effect.CurrentTechnique.Passes.Count != 1)
        {
            var message = $"Required shader '{assetName}' must expose exactly one pass; found {effect.CurrentTechnique.Passes.Count}.";
            log.Error(message);
            throw new ContentLoadException(message);
        }

        return effect;
    }

    private abstract class EffectBindingBase
    {
        protected readonly EffectParameter _view;
        protected readonly EffectParameter _projection;
        protected readonly EffectParameter _ambientColor;
        protected readonly EffectParameter _sunDirection;
        protected readonly EffectParameter _sunColor;
        protected readonly EffectParameter _lightViewProjection;
        protected readonly EffectParameter _shadowMap;
        protected readonly EffectParameter _shadowBias;
        protected readonly EffectParameter _shadowMaxSlopeBias;
        protected readonly EffectParameter _shadowNormalOffset;
        protected readonly EffectParameter _shadowStrength;
        protected readonly EffectParameter _shadowTexelSize;
        protected readonly EffectParameter _shadowsEnabled;
        protected readonly EffectParameter _receivesShadow;
        protected readonly EffectParameter _pointLightCount;
        private readonly EffectParameter _transientLightCount;
        protected readonly EffectParameter _pointLightPositions;
        protected readonly EffectParameter _pointLightColors;
        protected readonly EffectParameter _texture;
        protected readonly EffectParameter _alphaCutoff;
        protected readonly EffectParameter _uvScrollOffset;
        protected readonly EffectParameter _texture2;
        protected readonly EffectParameter _uvScrollOffset2;
        protected readonly EffectParameter _secondTextureMode;
        protected readonly EffectParameter _selfIllum;
        protected readonly EffectParameter _lampFactor;
        protected readonly EffectParameter _blinkPulse;
        protected readonly EffectParameter _materialBlink;
        protected readonly EffectParameter _vertexAnimTime;
        protected readonly EffectParameter _materialWaterRipple;
        protected readonly EffectParameter _windParams;
        protected readonly EffectParameter _cameraPosition;
        protected readonly EffectParameter _materialReflex;
        private float _baseShadowNormalOffset;
        private Texture2D _depthTexture;
        private Vector2 _depthUvScrollOffset;
        private Vector4 _depthWindParams;
        private bool _depthWaterRipple;
        private bool _hasDepthTexture;
        private bool _hasDepthUvScrollOffset;
        private bool _hasDepthVertexAnimation;

        public Effect Effect { get; }

        protected EffectBindingBase(Effect effect)
        {
            Effect = effect;
            _view = effect.Parameters["View"];
            _projection = effect.Parameters["Projection"];
            _ambientColor = effect.Parameters["AmbientColor"];
            _sunDirection = effect.Parameters["SunDirection"];
            _sunColor = effect.Parameters["SunColor"];
            _lightViewProjection = effect.Parameters["LightViewProjection"];
            _shadowMap = effect.Parameters["ShadowMap"];
            _shadowBias = effect.Parameters["ShadowBias"];
            _shadowMaxSlopeBias = effect.Parameters["ShadowMaxSlopeBias"];
            _shadowNormalOffset = effect.Parameters["ShadowNormalOffset"];
            _shadowStrength = effect.Parameters["ShadowStrength"];
            _shadowTexelSize = effect.Parameters["ShadowTexelSize"];
            _shadowsEnabled = effect.Parameters["ShadowsEnabled"];
            _receivesShadow = effect.Parameters["ReceivesShadow"];
            _pointLightCount = effect.Parameters["PointLightCount"];
            _transientLightCount = effect.Parameters["TransientLightCount"];
            _pointLightPositions = effect.Parameters["PointLightPositions"];
            _pointLightColors = effect.Parameters["PointLightColors"];
            _texture = effect.Parameters["Texture"];
            _alphaCutoff = effect.Parameters["AlphaCutoff"];
            _uvScrollOffset = effect.Parameters["UvScrollOffset"];
            _texture2 = effect.Parameters["Texture2"];
            _uvScrollOffset2 = effect.Parameters["UvScrollOffset2"];
            _secondTextureMode = effect.Parameters["SecondTextureMode"];
            _selfIllum = effect.Parameters["SelfIllum"];
            _lampFactor = effect.Parameters["LampFactor"];
            _blinkPulse = effect.Parameters["BlinkPulse"];
            _materialBlink = effect.Parameters["MaterialBlink"];
            _vertexAnimTime = effect.Parameters["VertexAnimTime"];
            _materialWaterRipple = effect.Parameters["MaterialWaterRipple"];
            _windParams = effect.Parameters["WindParams"];
            _cameraPosition = effect.Parameters["CameraPosition"];
            _materialReflex = effect.Parameters["MaterialReflex"];
        }

        // One-time constant uniforms (never change at runtime). The slope-scaled depth bias keeps
        // the shadow smooth and acne-free. Halved from the original 0.0012/0.0035 because the 8192²
        // shadow map (vs 4096²) has half-size texels — same acne margin, half the peter-panning.
        public void SetConstantParameters(Vector2 shadowTexelSize, float shadowNormalOffset)
        {
            _baseShadowNormalOffset = shadowNormalOffset;
            _shadowBias?.SetValue(0.0006f);
            _shadowMaxSlopeBias?.SetValue(0.00175f);
            _shadowNormalOffset?.SetValue(shadowNormalOffset);
            _shadowTexelSize?.SetValue(shadowTexelSize);
            _receivesShadow?.SetValue(true);
            _alphaCutoff?.SetValue(60f / 255f); // original alpha-test ref (60/255)
        }

        protected void ApplyCommonFrame(PristonFrameContext frame, Vector4[] pointLightPositions, Vector4[] pointLightColors)
        {
            _transientLightCount?.SetValue(frame.Lighting.TransientLightCount);
            _view?.SetValue(frame.View);
            _projection?.SetValue(frame.Projection);
            _ambientColor?.SetValue(frame.Lighting.AmbientColor);
            _sunDirection?.SetValue(frame.Lighting.Sun.Direction);
            _sunColor?.SetValue(frame.Lighting.Sun.DiffuseColor);
            _lightViewProjection?.SetValue(frame.LightViewProjection);
            _shadowMap?.SetValue(frame.ShadowMap);
            if (frame.ShadowMap is not null)
            {
                var texelSize = new Vector2(1f / frame.ShadowMap.Width, 1f / frame.ShadowMap.Height);
                var biasScale = 8192f / frame.ShadowMap.Width;
                _shadowTexelSize?.SetValue(texelSize);
                _shadowBias?.SetValue(0.0006f * biasScale);
                _shadowMaxSlopeBias?.SetValue(0.00175f * biasScale);
                _shadowNormalOffset?.SetValue(_baseShadowNormalOffset);
            }
            _shadowsEnabled?.SetValue(frame.ShadowsEnabled);
            _lampFactor?.SetValue(frame.LampFactor);
            // Sun shadows fade out with the sun.
            _shadowStrength?.SetValue(0.88f * (1f - frame.LampFactor));
            var cap = pointLightPositions?.Length ?? 0;
            _pointLightCount?.SetValue(Math.Min(frame.Lighting.PointLights.Count, cap));
            if (pointLightPositions != null)
                _pointLightPositions?.SetValue(pointLightPositions);
            if (pointLightColors != null)
                _pointLightColors?.SetValue(pointLightColors);

            const double blinkPeriodMs = 1200.0;
            var pulse = 0.35f + 0.65f * (0.5f + 0.5f * (float)System.Math.Sin(frame.RenderTime * (2.0 * System.Math.PI / blinkPeriodMs)));
            _blinkPulse?.SetValue(pulse);

            SetVertexAnimTime(frame.RenderTime);

            _cameraPosition?.SetValue(frame.CameraPosition);
        }

        // Wrapped seconds clock so float sin() stays precise over long runs. Shared by the colour
        // (ApplyCommonFrame) and depth (ApplyDepthFrame) paths so both displace vertices in lockstep.
        public void SetVertexAnimTime(uint renderTimeMs)
            => _vertexAnimTime?.SetValue((float)((renderTimeMs % 100000u) / 1000.0));

        protected void ApplyMaterialCore(Texture2D texture, PristonMaterialRuntime material, Vector2 uvScrollOffset, Vector4 windParams, Vector2 uvScrollOffset2)
        {
            _texture?.SetValue(texture);
            _uvScrollOffset?.SetValue(uvScrollOffset);
            _selfIllum?.SetValue(material.SelfIllum ? 1f : 0f);
            _materialBlink?.SetValue(material.Blinking ? 1f : 0f);
            _materialWaterRipple?.SetValue(material.Water ? 1f : 0f);
            _windParams?.SetValue(windParams);
            _materialReflex?.SetValue(material.Reflex ? 1f : 0f);

            if (material.HasSecondTexture)
            {
                _texture2?.SetValue(material.SecondTexture);
                _uvScrollOffset2?.SetValue(uvScrollOffset2);
                _secondTextureMode?.SetValue(MultiMixModeAdd);
            }
            else
            {
                _secondTextureMode?.SetValue(MultiMixModeOff);
            }
        }

        public void ApplyDepthVertexAnimation(bool water, Vector4 windParams)
        {
            if (!_hasDepthVertexAnimation || _depthWaterRipple != water)
            {
                _materialWaterRipple?.SetValue(water ? 1f : 0f);
                _depthWaterRipple = water;
            }

            if (!_hasDepthVertexAnimation || _depthWindParams != windParams)
            {
                _windParams?.SetValue(windParams);
                _depthWindParams = windParams;
            }

            _hasDepthVertexAnimation = true;
        }

        public void ApplyDepthAlphaTestMaterial(Texture2D texture, Vector2 uvScrollOffset, bool water, Vector4 windParams)
        {
            if (!_hasDepthTexture || !ReferenceEquals(_depthTexture, texture))
            {
                _texture?.SetValue(texture);
                _depthTexture = texture;
                _hasDepthTexture = true;
            }

            if (!_hasDepthUvScrollOffset || _depthUvScrollOffset != uvScrollOffset)
            {
                _uvScrollOffset?.SetValue(uvScrollOffset);
                _depthUvScrollOffset = uvScrollOffset;
                _hasDepthUvScrollOffset = true;
            }

            ApplyDepthVertexAnimation(water, windParams);
        }

        private const float MultiMixModeOff = 0f;
        private const float MultiMixModeAdd = 1f;
    }

    private sealed class EffectBinding : EffectBindingBase
    {
        private readonly EffectParameter _world;
        private readonly EffectParameter _drawOpacity;
        private readonly EffectParameter _lampShadowCount;
        private readonly EffectParameter _lampShadowTexel;
        private readonly EffectParameter _lampShadowAtlas;
        private readonly EffectParameter _lampShadowVP;
        private readonly EffectParameter _lampShadowPos;
        private readonly EffectParameter _lampShadowTile;

        // Reused scratch arrays (sized to the atlas tile count) to avoid per-frame allocation.
        private readonly Matrix[] _lampVP = new Matrix[LampShadowSystem.MaxLamps];
        private readonly Vector4[] _lampPos = new Vector4[LampShadowSystem.MaxLamps];
        private readonly Vector4[] _lampTile = new Vector4[LampShadowSystem.MaxLamps];

        public EffectBinding(Effect effect) : base(effect)
        {
            _world = effect.Parameters["World"];
            _drawOpacity = effect.Parameters["DrawOpacity"];
            _lampShadowCount = effect.Parameters["LampShadowCount"];
            _lampShadowTexel = effect.Parameters["LampShadowTexel"];
            _lampShadowAtlas = effect.Parameters["LampShadowAtlas"];
            _lampShadowVP = effect.Parameters["LampShadowVP"];
            _lampShadowPos = effect.Parameters["LampShadowPos"];
            _lampShadowTile = effect.Parameters["LampShadowTile"];
        }

        public void SetLampShadows(LampShadowSystem lamps)
        {
            _lampShadowCount?.SetValue(lamps.Count);
            if (lamps.Count == 0)
                return;
            for (var i = 0; i < lamps.Count; i++)
            {
                _lampVP[i] = lamps.ViewProjection(i);
                _lampPos[i] = lamps.PositionRadius(i);
                _lampTile[i] = lamps.Tile(i);
            }
            _lampShadowTexel?.SetValue(lamps.TexelSize);
            _lampShadowAtlas?.SetValue(lamps.Atlas);
            _lampShadowVP?.SetValue(_lampVP);
            _lampShadowPos?.SetValue(_lampPos);
            _lampShadowTile?.SetValue(_lampTile);
        }

        public void ApplyFrame(PristonFrameContext frame) => ApplyFrame(frame, null, null);

        public void ApplyFrame(PristonFrameContext frame, Vector4[] pointLightPositions, Vector4[] pointLightColors)
        {
            _world?.SetValue(Matrix.Identity);
            ApplyCommonFrame(frame, pointLightPositions, pointLightColors);
        }

        public void ApplyMatrices(Matrix world, Matrix view, Matrix projection)
        {
            _world?.SetValue(world);
            _view?.SetValue(view);
            _projection?.SetValue(projection);
        }

        public void ApplyMaterial(Texture2D texture, PristonMaterialRuntime material, Vector2 uvScrollOffset, Vector4 windParams, Vector2 uvScrollOffset2, float opacity = 1f)
        {
            _drawOpacity?.SetValue(opacity);
            ApplyMaterialCore(texture, material, uvScrollOffset, windParams, uvScrollOffset2);
        }
    }

    private sealed class SkinnedEffectBinding : EffectBindingBase
    {
        private readonly EffectParameter _bonePalette;
        private readonly EffectParameter _actorColorAdd;
        private PristonSkinPalette _currentPalette;
        private uint _currentPaletteRevision;
        private Vector3 _currentActorColorAdd;
        private bool _hasActorColorAdd;

        public SkinnedEffectBinding(Effect effect) : base(effect)
        {
            _bonePalette = effect.Parameters["BonePalette"];
            _actorColorAdd = effect.Parameters["ActorColorAdd"];
        }

        public void ApplyFrame(PristonFrameContext frame) => ApplyFrame(frame, null, null);

        public void ApplyFrame(PristonFrameContext frame, Vector4[] pointLightPositions, Vector4[] pointLightColors)
            => ApplyCommonFrame(frame, pointLightPositions, pointLightColors);

        public void ApplyViewProjection(Matrix view, Matrix projection)
        {
            _view?.SetValue(view);
            _projection?.SetValue(projection);
        }

        // Packing happens once per pose in the owner, not once per material and shadow pass.
        public void SetBonePalette(PristonSkinPalette palette)
        {
            if (ReferenceEquals(_currentPalette, palette)
                && _currentPaletteRevision == palette.Revision)
            {
                return;
            }

            _bonePalette?.SetValue(palette.ShaderRows);
            _currentPalette = palette;
            _currentPaletteRevision = palette.Revision;
        }

        public void SetActorColorAdd(Vector3 colorAdd)
        {
            if (_hasActorColorAdd && _currentActorColorAdd == colorAdd)
            {
                return;
            }

            _actorColorAdd?.SetValue(colorAdd);
            _currentActorColorAdd = colorAdd;
            _hasActorColorAdd = true;
        }

        public void ApplyMaterial(Texture2D texture, PristonMaterialRuntime material, Vector2 uvScrollOffset)
            => ApplyMaterialCore(texture, material, uvScrollOffset, Vector4.Zero, Vector2.Zero);
    }
}
