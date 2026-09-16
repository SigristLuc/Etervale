using System.Runtime.Intrinsics;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Tests;

internal static class LampTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("scalar and supported SIMD lamp kernels preserve reference colour bytes", EquivalenceAsync);
    }

    private static Task EquivalenceAsync()
    {
        var random = new Random(904);
        foreach (var count in new[] { 0, 1, 3, 4, 7, 8, 15, 16, 17, 72, 193 })
        {
            var lights = LampBenchmark.Lights(count);
            foreach (var mode in Enum.GetValues<LampKernel>())
            {
                var baker = new PristonLampBaker(lights, mode);
                Assert.True(baker.Kernel != LampKernel.Vector256 || Vector256.IsHardwareAccelerated, "unsupported 256-bit dispatch");
                Assert.True(baker.Kernel != LampKernel.Vector128 || Vector128.IsHardwareAccelerated, "unsupported 128-bit dispatch");
                for (var sample = 0; sample < 2000; sample++)
                {
                    var position = new Vector3(random.NextSingle() * 8000, random.NextSingle() * 200, random.NextSingle() * 8000);
                    var normal = Vector3.Normalize(new Vector3(random.NextSingle() - 0.5f, random.NextSingle(), random.NextSingle() - 0.5f));
                    Assert.True(baker.Bake(position, normal) == Reference(lights, position, normal), $"{mode} changed a quantized lamp colour");
                }

                foreach (var light in lights)
                {
                    foreach (var radius in new[] { 0f, MathF.BitDecrement(light.Range), light.Range, MathF.BitIncrement(light.Range) })
                    {
                        var position = light.Position + new Vector3(radius, 0, 0);
                        Assert.True(baker.Bake(position, Vector3.Up) == Reference(lights, position, Vector3.Up), $"{mode} changed a range boundary");
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    // The previous production algorithm, retained as an independent numerical oracle.
    internal static Color Reference(PristonGpuMeshBuilder.BakeLight[] lights, Vector3 position, Vector3 normal)
    {
        var sum = Vector3.Zero;
        foreach (var light in lights)
        {
            if (light.Range <= 0f)
            {
                continue;
            }

            var toLight = light.Position - position;
            var distance = toLight.Length();
            if (distance >= light.Range)
            {
                continue;
            }

            var attenuation = 1f - distance / light.Range;
            attenuation *= attenuation;
            var cosine = distance > 1e-4f ? MathHelper.Clamp(Vector3.Dot(normal, toLight / distance), 0f, 1f) : 1f;
            sum += light.Color * (attenuation * (0.25f + 0.75f * cosine));
        }

        return new Color(Vector3.Clamp(sum, Vector3.Zero, Vector3.One));
    }
}
