using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal static class ShadowProjectionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("sun projection locks a world anchor to texels while the camera moves", StabilizationAsync);
        yield return ("perspective shadow depth divides after homogeneous interpolation", PerspectiveDepthAsync);
        yield return ("shadow refresh rate is independent of presentation and handles clock wrap", RefreshRateAsync);
    }

    private static Task StabilizationAsync()
    {
        var direction = Vector3.Normalize(new Vector3(-0.3f, -0.8f, -0.45f));
        var projection = Matrix.CreateOrthographic(2800, 2800, 10, 5600);
        foreach (var size in new[] { 1024, 2048, 4096, 8192 })
        {
            for (var step = 0; step < 50; step++)
            {
                var center = new Vector3(1234.567f + step * 0.013f, 40, -3456.78f + step * 0.017f);
                var view = Matrix.CreateLookAt(center - direction * 2520, center, Vector3.Up);
                var stabilized = PristonRenderer.StabilizeShadowProjection(view, projection, size);
                var origin = Vector4.Transform(Vector4.UnitW, view * stabilized);
                var texelX = (origin.X * 0.5f + 0.5f) * size;
                var texelY = (origin.Y * 0.5f + 0.5f) * size;
                Assert.Near(MathF.Round(texelX), texelX, 0.002f);
                Assert.Near(MathF.Round(texelY), texelY, 0.002f);
                Assert.True(stabilized.M33 == projection.M33 && stabilized.M43 == projection.M43, "stabilization changed depth mapping");
            }
        }

        return Task.CompletedTask;
    }

    private static Task PerspectiveDepthAsync()
    {
        // Unequal light-space w, as with a slanted triangle under a point lamp.
        // Hardware depth is screen-linear in z/w; an interpolator is perspective-correct in its inputs.
        float[] z = [1, 3, 9];
        float[] w = [2, 4, 10];
        float[] barycentric = [0.2f, 0.3f, 0.5f];
        var reciprocalW = 0f;
        var numeratorZ = 0f;
        var numeratorW = 0f;
        var dividedAtVertex = 0f;
        for (var index = 0; index < 3; index++)
        {
            var weight = barycentric[index] / w[index];
            reciprocalW += weight;
            numeratorZ += weight * z[index];
            numeratorW += weight * w[index];
            dividedAtVertex += weight * (z[index] / w[index]);
        }

        var interpolatedZ = numeratorZ / reciprocalW;
        var interpolatedW = numeratorW / reciprocalW;
        Assert.Near(0.775f, interpolatedZ / interpolatedW);
        Assert.True(MathF.Abs(dividedAtVertex / reciprocalW - 0.775f) > 0.01f, "fixture must distinguish the old perspective-depth error");
        return Task.CompletedTask;
    }

    private static Task RefreshRateAsync()
    {
        var scheduler = new FixedRateRefreshScheduler();
        var updates = 0;
        for (uint timestamp = 0; timestamp <= 1000; timestamp += 4)
        {
            if (scheduler.ShouldRefresh(timestamp, 60, enabled: true))
            {
                updates++;
            }
        }

        Assert.True(updates is >= 60 and <= 62, $"60 Hz shadow cadence produced {updates} updates");
        Assert.True(scheduler.ShouldRefresh(1004, 0, enabled: true), "per-frame shadow mode skipped a frame");
        Assert.True(scheduler.ShouldRefresh(1008, 0, enabled: true), "per-frame shadow mode retained a cap");
        Assert.True(!scheduler.ShouldRefresh(1012, 60, enabled: false), "disabled shadows requested an update");
        Assert.True(scheduler.ShouldRefresh(1016, 60, enabled: true), "re-enabled shadows reused invalid content");

        scheduler = new FixedRateRefreshScheduler();
        Assert.True(scheduler.ShouldRefresh(uint.MaxValue - 5, 60, enabled: true), "first wrapped-clock frame was not rendered");
        Assert.True(!scheduler.ShouldRefresh(2, 60, enabled: true), "clock wrap created an early refresh");
        Assert.True(scheduler.ShouldRefresh(12, 60, enabled: true), "clock wrap prevented the scheduled refresh");
        scheduler.Invalidate();
        Assert.True(scheduler.ShouldRefresh(13, 60, enabled: true), "invalidated shadow content was not rebuilt immediately");
        return Task.CompletedTask;
    }
}
