using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal sealed class ShaderValidationProbe : Game
{
    private bool _completed;

    public ShaderValidationProbe(string contentRoot)
    {
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 800,
            PreferredBackBufferHeight = 600,
            GraphicsProfile = GraphicsProfile.HiDef
        };
        Content.RootDirectory = Path.GetFullPath(contentRoot);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_completed)
        {
            return;
        }

        var pipeline = new PristonEffectPipeline(Content, ConsoleClientLog.Instance);
        using var texture = new Texture2D(GraphicsDevice, 2, 1);
        texture.SetData(new[] { Color.White, Color.Transparent });
        using var target = new RenderTarget2D(GraphicsDevice, 17, 17, false, SurfaceFormat.Single, DepthFormat.None);
        var palette = new PristonSkinPalette(1);
        palette.Matrices[0] = Matrix.Identity;
        palette.Pack();
        var frame = new PristonFrameContext(Matrix.Identity, Matrix.Identity, Vector3.Backward, 0,
            new PristonLighting(Vector3.Zero, new PristonDirectionalLight(Vector3.Down, Vector3.Zero), []),
            Matrix.Identity, texture, false, 0);
        var pixels = new float[17 * 17];
        var verified = 0;
        foreach (var skinned in new[] { false, true })
        {
            foreach (var alphaTest in new[] { false, true })
            {
                foreach (var scroll in new[] { 0f, 0.5f })
                {
                    var offset = new Vector2(scroll, 0);
                    foreach (var selfIllum in new[] { false, true })
                    {
                        var material = Material(texture, alphaTest, selfIllum);
                        var actorColorAdd = skinned ? new Vector3(0.2f, 0.1f, 0.05f) : Vector3.Zero;
                        pipeline.ApplyFrame(frame);
                        var effect = skinned
                            ? pipeline.SelectSkinnedEffect(material, texture, offset, palette, actorColorAdd)
                            : pipeline.SelectEffect(material, texture, offset, Vector4.Zero, Vector2.Zero);
                        if (skinned)
                        {
                            Assert.True(effect.Parameters["ActorColorAdd"].GetValueVector3() == actorColorAdd, "actor additive tint was not bound");
                        }
                        var discarded = alphaTest && scroll != 0;
                        var expected = 0f;
                        if (discarded)
                        {
                            expected = 0.25f;
                        }
                        else if (selfIllum && scroll == 0)
                        {
                            expected = 1f;
                        }
                        Verify(effect, skinned, expected, target, pixels, $"colour skin={skinned} alpha={alphaTest} scroll={scroll} emissive={selfIllum}");
                        verified++;
                    }

                    pipeline.ApplyDepthFrame(Matrix.Identity, Matrix.Identity, 0);
                    var caster = Material(texture, alphaTest, false);
                    var depth = SelectDepthEffect(pipeline, caster, texture, palette, offset, skinned);
                    Assert.True(
                        (depth.Parameters["Texture"] is not null) == alphaTest,
                        $"depth texture contract mismatch for skin={skinned} alpha={alphaTest}");
                    Assert.True(
                        (depth.Parameters["UvScrollOffset"] is not null) == alphaTest,
                        $"depth UV contract mismatch for skin={skinned} alpha={alphaTest}");
                    var depthExpected = alphaTest && scroll != 0 ? 0.25f : 0.5f;
                    Verify(depth, skinned, depthExpected, target, pixels, $"depth skin={skinned} alpha={alphaTest} scroll={scroll}");
                    verified++;
                }
            }
        }

        // Odd target dimensions put this sample exactly at NDC (0, 0), independently of Y inversion.
        // Screen barycentrics are (1/4, 1/4, 1/2); projected depth must interpolate linearly there.
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1, 1, 20);
        var nearClip = Vector4.Transform(new Vector4(0, 0, -2, 1), projection);
        var farClip = Vector4.Transform(new Vector4(0, 0, -10, 1), projection);
        var expectedDepth = 0.5f * (nearClip.Z / nearClip.W + farClip.Z / farClip.W);
        Vector3[] triangle = [new(-2, -2, -2), new(2, -2, -2), new(0, 10, -10)];
        foreach (var skinned in new[] { false, true })
        {
            pipeline.ApplyDepthFrame(Matrix.Identity, projection, 0);
            var caster = Material(texture, false, false);
            var effect = SelectDepthEffect(pipeline, caster, texture, palette, Vector2.Zero, skinned);
            Verify(effect, skinned, expectedDepth, target, pixels, $"perspective depth skin={skinned}", triangle);
            verified++;
        }

        using var occludedShadowMap = new RenderTarget2D(GraphicsDevice, 17, 17, false, SurfaceFormat.Single, DepthFormat.None);
        GraphicsDevice.SetRenderTarget(occludedShadowMap);
        GraphicsDevice.Clear(Color.Black);
        GraphicsDevice.SetRenderTarget(null);
        var shadowFrame = new PristonFrameContext(
            Matrix.Identity,
            Matrix.Identity,
            Vector3.Backward,
            0,
            new PristonLighting(new Vector3(0.2f), new PristonDirectionalLight(Vector3.Down, Vector3.One), []),
            Matrix.Identity,
            occludedShadowMap,
            true,
            0);
        foreach (var skinned in new[] { false, true })
        {
            pipeline.ApplyFrame(shadowFrame);
            var receiver = Material(texture, false, false);
            var effect = skinned
                ? pipeline.SelectSkinnedEffect(receiver, texture, Vector2.Zero, palette, Vector3.Zero)
                : pipeline.SelectEffect(receiver, texture, Vector2.Zero, Vector4.Zero, Vector2.Zero);
            Assert.True(effect.Parameters["ShadowsEnabled"].GetValueBoolean(), $"sun shadow flag was not bound for skin={skinned}");
            Assert.True(effect.Parameters["ReceivesShadow"].GetValueBoolean(), $"sun shadow receiver flag was not bound for skin={skinned}");
            Assert.True(effect.Parameters["ShadowNormalOffset"] is not null, $"sun shadow normal offset was not compiled for skin={skinned}");
            Assert.Near(0.88f, effect.Parameters["ShadowStrength"].GetValueSingle());
            Assert.Near(skinned ? 1.25f : 0f, effect.Parameters["ShadowNormalOffset"].GetValueSingle());
            Verify(effect, skinned, 0.32f, target, pixels, $"sun shadow receiver skin={skinned}");
            verified++;
        }

        Console.WriteLine($"SHADERS {verified}/{verified} GPU checks passed; {GraphicsDevice.Adapter.Description}; Content={Content.RootDirectory}");
        _completed = true;
        Exit();
    }

    private static Effect SelectDepthEffect(
        PristonEffectPipeline pipeline,
        PristonMaterialRuntime material,
        Texture2D texture,
        PristonSkinPalette palette,
        Vector2 uvScrollOffset,
        bool skinned)
    {
        if (skinned)
        {
            return material.AlphaTest
                ? pipeline.SelectSkinnedAlphaTestDepthEffect(texture, palette, uvScrollOffset)
                : pipeline.SelectSkinnedOpaqueDepthEffect(palette);
        }

        return material.AlphaTest
            ? pipeline.SelectAlphaTestDepthEffect(material, texture, Vector4.Zero, uvScrollOffset)
            : pipeline.SelectOpaqueDepthEffect(material, Vector4.Zero);
    }

    private void Verify(Effect effect, bool skinned, float expected, RenderTarget2D target, float[] pixels, string scenario, Vector3[]? positions = null)
    {
        GraphicsDevice.SetRenderTarget(target);
        GraphicsDevice.Clear(new Color(new Vector4(0.25f)));
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        positions ??= [new(-1, -1, 0.5f), new(1, -1, 0.5f), new(1, 1, 0.5f), new(-1, -1, 0.5f), new(1, 1, 0.5f), new(-1, 1, 0.5f)];
        var uv = new Vector2(0.25f, 0.5f);
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (skinned)
            {
                var vertices = positions.Select(position => new PristonSkinnedVertex(position, Vector3.Up, Color.White, uv, 0)).ToArray();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3);
            }
            else
            {
                var vertices = positions.Select(position => new PristonStaticVertex(position, Vector3.Up, Color.White, uv, Color.Black)).ToArray();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3);
            }
        }

        GraphicsDevice.SetRenderTarget(null);
        target.GetData(pixels);
        var actual = pixels[8 * 17 + 8];
        Assert.True(float.IsFinite(actual) && MathF.Abs(expected - actual) < 0.005f, $"{scenario}: expected {expected}, got {actual}");
    }

    private static PristonMaterialRuntime Material(Texture2D texture, bool alphaTest, bool selfIllum)
    {
        return new PristonMaterialRuntime([texture], twoSided: true, transparent: false, alphaTest: alphaTest,
            blendType: 0, transparency: 0, textureFormState: 0, frameMask: 0, shiftFrameSpeed: 0, animationFrame: 0,
            selfIllum: selfIllum, invisible: false, water: false, blinking: false, windAmplitudeX: 0, windAmplitudeZ: 0,
            reflex: false, secondTexture: null!, secondTextureFormState: 0);
    }
}
