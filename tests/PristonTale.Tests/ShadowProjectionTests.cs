using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Tests;

internal static class ShadowProjectionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("sun cascades move by whole texels with the camera and pivot about the focus as the sun turns", StabilizationAsync);
        yield return ("perspective shadow depth divides after homogeneous interpolation", PerspectiveDepthAsync);
        yield return ("shadow refresh rate is independent of presentation and handles clock wrap", RefreshRateAsync);
    }

    private static Task StabilizationAsync()
    {
        var direction = Vector3.Normalize(new Vector3(-0.3f, -0.8f, -0.45f));
        foreach (var worldPerTexel in new[] { 0.34f, 0.68f, 1.37f })
        {
            // The camera moves: a still point keeps its place in the texel grid (the same sub-texel phase).
            var snap = default(PristonShadowCascades.FocusSnap);
            var point = new Vector3(21800.25f, 40f, -10450.5f);
            double? phase = null;
            for (var step = 0; step < 200; step++)
            {
                var focus = point + new Vector3(step * 0.731f, 0f, -step * 0.417f);
                var centre = PristonShadowCascades.FollowFocus(ref snap, focus, direction, Vector3.Up, worldPerTexel);
                var right = Vector3.Normalize(Vector3.Cross(Vector3.Up, -direction));
                var texel = Vector3.Dot(point - centre, right) / worldPerTexel;
                var fraction = texel - Math.Floor(texel);
                phase ??= fraction;
                var drift = Math.Abs(fraction - phase.Value);
                Assert.True(Math.Min(drift, 1.0 - drift) < 0.01, $"the grid slid {drift} texel under a still point");
            }

            // The sun turns: the centre stays where it is, so the grid pivots about the focus instead of jumping.
            snap = default;
            var still = new Vector3(21800.25f, 40f, -10450.5f);
            var first = PristonShadowCascades.FollowFocus(ref snap, still, direction, Vector3.Up, worldPerTexel);
            for (var step = 1; step < 100; step++)
            {
                var turned = Vector3.Transform(direction, Matrix.CreateRotationY(step * 0.0001f));
                var centre = PristonShadowCascades.FollowFocus(ref snap, still, turned, Vector3.Up, worldPerTexel);
                Assert.True(Vector3.Distance(first, centre) < 1e-3f, $"the centre moved {Vector3.Distance(first, centre)} as the sun turned");
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
