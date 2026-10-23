using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Engine3D.Rendering;

// The skill effect shader (Shaders/Passes/Vfx.fx): HDR sprites with linear vertex colours and the soft-particle fade
// against the scene's linear depth. Each owner holds its own effect instance.
public sealed class PristonVfxEffect : IDisposable
{
    public const string AssetName = "Effects/Vfx";

    private readonly Effect _effect;
    private readonly EffectParameter _viewProjection;
    private readonly EffectParameter _texture;
    private readonly EffectParameter _depth;
    private readonly EffectParameter _exposure;
    private readonly EffectParameter _displayOutput;
    private readonly EffectParameter _softParticles;

    public PristonVfxEffect(ContentManager content, IClientLog log = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        _effect = PristonShaderLibrary.LoadRequired(content, AssetName, log ?? ConsoleClientLog.Instance);
        _viewProjection = _effect.Parameters["ViewProjection"];
        _texture = _effect.Parameters["Texture"];
        _depth = _effect.Parameters["DepthMap"];
        _exposure = _effect.Parameters["Exposure"];
        _displayOutput = _effect.Parameters["DisplayOutput"];
        _softParticles = _effect.Parameters["SoftParticles"];
    }

    public Matrix ViewProjection { get; set; } = Matrix.Identity;

    public Texture2D Texture { get; set; }

    // The scene's linear view depth (PristonRenderer.SceneLinearDepth); null turns the soft-particle fade off.
    public Texture2D Depth { get; set; }

    public float? Exposure { get; set; }

    public bool DisplayOutput { get; set; }

    public void Apply()
    {
        _viewProjection.SetValue(ViewProjection);
        _texture.SetValue(Texture);
        _depth?.SetValue(Depth);
        _softParticles?.SetValue(Depth is null ? 0f : 1f);
        _exposure?.SetValue(Exposure ?? PristonUnlitEffect.SceneExposure);
        _displayOutput?.SetValue(DisplayOutput ? 1f : 0f);
        _effect.CurrentTechnique.Passes[0].Apply();
        _effect.GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
        _effect.GraphicsDevice.SamplerStates[1] = SamplerState.PointClamp;
    }

    public void Dispose() => _effect.Dispose();
}
