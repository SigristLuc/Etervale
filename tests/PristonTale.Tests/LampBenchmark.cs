using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Lighting;
using System.Runtime.Intrinsics;

namespace PristonTale.Tests;

internal static class LampBenchmark
{
    internal static PristonGpuMeshBuilder.BakeLight[] Lights(int count)
    {
        var random = new Random(117);
        var lights = new PristonGpuMeshBuilder.BakeLight[count];
        for (var i = 0; i < count; i++)
        {
            var position = new Vector3(random.NextSingle() * 8000f, random.NextSingle() * 200f, random.NextSingle() * 8000f);
            lights[i] = new(position, new Vector3(0.3f, 0.2f, 0.1f), 500f + random.NextSingle() * 1000f);
        }

        return lights;
    }

    public static void Run()
    {
        Console.WriteLine($"LAMP {RuntimeInformation.FrameworkDescription} {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"LAMP runtime Vector128={Vector128.IsHardwareAccelerated} Vector256={Vector256.IsHardwareAccelerated}");
        var random = new Random(213);
        var vertices = new Vector3[20000];
        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new Vector3(random.NextSingle() * 8000f, 0f, random.NextSingle() * 8000f);
        }

        foreach (var count in new[] { 6, 72, 192 })
        {
            var builder = new PristonGpuMeshBuilder(null!);
            builder.SetBakeLights(Lights(count));
            Measure($"production/{builder.LampKernel} lights={count}", vertices, builder.BakeLamp);
            var lights = Lights(count);
            Measure($"reference lights={count}", vertices, (position, normal) => LampTests.Reference(lights, position, normal));
            foreach (var mode in Enum.GetValues<LampKernel>())
            {
                var baker = new PristonLampBaker(lights, mode);
                if (baker.Kernel == mode)
                {
                    Measure($"explicit/{mode} lights={count}", vertices, baker.Bake);
                }
            }
        }
    }

    internal static void Measure(string label, Vector3[] vertices, Func<Vector3, Vector3, Color> bake)
    {
        uint checksum = 0;
        for (var warmup = 0; warmup < 10; warmup++)
        {
            foreach (var vertex in vertices)
            {
                checksum = unchecked(checksum + bake(vertex, Vector3.Up).PackedValue);
            }
        }

        for (var run = 0; run < 5; run++)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            for (var repeat = 0; repeat < 10; repeat++)
            {
                foreach (var vertex in vertices)
                {
                    checksum = unchecked(checksum + bake(vertex, Vector3.Up).PackedValue);
                }
            }

            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            Console.WriteLine($"LAMP {label} run={run + 1} vertices={vertices.Length * 10} ms={elapsed:F3} bytes={allocated} checksum={checksum}");
        }
    }
}
