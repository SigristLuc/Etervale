using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Engine3D.Rendering.Post;

namespace PristonTale.Tests;

// GPU check of the image-based lighting: a uniform grey dome gives an irradiance whose upward value is the light
// model's sky light, whatever the dome's own level and the windowing of its bands, and the specular atlas's scale is
// that ratio.
internal sealed partial class ShaderValidationProbe
{
    private void VerifyEnvironment()
    {
        using var triangle = new PristonFullscreenTriangle(GraphicsDevice);
        using var effect = Content.Load<Effect>(PristonEnvironmentLighting.Asset).Clone();
        using var environment = new PristonEnvironmentLighting(GraphicsDevice, triangle, effect);
        var sky = new Vector3(0.2f, 0.25f, 0.3f);
        var ground = new Vector3(0.05f);
        environment.BeginFaces(new Color(128, 128, 128));
        environment.Build(0, 1f, sky, ground);
        GraphicsDevice.SetRenderTarget(null);
        var sh = new Vector4[PristonEnvironmentLighting.ShEntries];
        environment.Sh.GetData<Vector4>(sh.AsSpan());
        // Upward irradiance: band 0 and the y term (the uniform dome has no band-2 term).
        var up = new Vector3(sh[0].X, sh[0].Y, sh[0].Z) * 0.282095f + new Vector3(sh[1].X, sh[1].Y, sh[1].Z) * (0.488603f * 2f / 3f);
        // The ground adds nothing to an upward surface; the colour is 35 % the dome's grey, 65 % the model's.
        var greyLevel = 0.2126f * sky.X + 0.7152f * sky.Y + 0.0722f * sky.Z;
        var expectedUp = new Vector3(greyLevel) * 0.35f + sky * 0.65f;
        var radiance = SrgbToLinear(128f / 255f);
        var expectedScale = (0.2126f * sky.X + 0.7152f * sky.Y + 0.0722f * sky.Z) / radiance;
        // Order-2 harmonics underestimate a uniform hemisphere at the zenith by about 3 %.
        Assert.True(MathF.Abs(sh[9].X / expectedScale - 1f) < 0.05f, $"environment scale {sh[9].X}, expected {expectedScale}");
        Assert.True(Vector3.Distance(up, expectedUp) < 0.01f, $"environment upward irradiance {up}, expected {expectedUp}");
        _verified += 2;
    }
}
