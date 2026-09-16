using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Microsoft.Xna.Framework;
using Vector3 = Microsoft.Xna.Framework.Vector3;

namespace PristonTale.Engine3D.Rendering.Lighting;

internal enum LampKernel
{
    Scalar,
    Vector128,
    Vector256
}

internal sealed class PristonLampBaker
{
    private readonly PristonGpuMeshBuilder.BakeLight[] _lights;
    private readonly float[] _x;
    private readonly float[] _y;
    private readonly float[] _z;
    private readonly float[] _rangeSquared;

    public PristonLampBaker(PristonGpuMeshBuilder.BakeLight[] lights, LampKernel? requested = null)
    {
        _lights = (PristonGpuMeshBuilder.BakeLight[])lights.Clone();
        Kernel = SelectKernel(lights.Length, requested);
        _x = new float[lights.Length];
        _y = new float[lights.Length];
        _z = new float[lights.Length];
        _rangeSquared = new float[lights.Length];
        for (var i = 0; i < lights.Length; i++)
        {
            var light = lights[i];
            _x[i] = light.Position.X;
            _y[i] = light.Position.Y;
            _z[i] = light.Position.Z;
            _rangeSquared[i] = light.Range > 0f ? light.Range * light.Range : -1f;
        }
    }

    public LampKernel Kernel { get; }

    private static LampKernel SelectKernel(int count, LampKernel? requested)
    {
        if (requested == LampKernel.Scalar || (requested is null && count < 16))
        {
            return LampKernel.Scalar;
        }

        // The runtime incorporates CPU, OS and JIT support. Never infer ISA support from an architecture name.
        if (requested != LampKernel.Vector128 && Vector256.IsHardwareAccelerated)
        {
            return LampKernel.Vector256;
        }

        return Vector128.IsHardwareAccelerated ? LampKernel.Vector128 : LampKernel.Scalar;
    }

    public Color Bake(Vector3 position, Vector3 normal)
    {
        var sum = Vector3.Zero;
        var i = 0;
        if (Kernel == LampKernel.Vector256)
        {
            var px = Vector256.Create(position.X);
            var py = Vector256.Create(position.Y);
            var pz = Vector256.Create(position.Z);
            for (; i <= _lights.Length - Vector256<float>.Count; i += Vector256<float>.Count)
            {
                var dx = Vector256.LoadUnsafe(ref _x[i]) - px;
                var dy = Vector256.LoadUnsafe(ref _y[i]) - py;
                var dz = Vector256.LoadUnsafe(ref _z[i]) - pz;
                var distanceSquared = (dx * dx + dy * dy) + dz * dz;
                var candidates = Vector256.LessThanOrEqual(distanceSquared, Vector256.LoadUnsafe(ref _rangeSquared[i]));
                AccumulateMask(candidates.ExtractMostSignificantBits(), i, position, normal, ref sum);
            }
        }
        else if (Kernel == LampKernel.Vector128)
        {
            var px = Vector128.Create(position.X);
            var py = Vector128.Create(position.Y);
            var pz = Vector128.Create(position.Z);
            for (; i <= _lights.Length - Vector128<float>.Count; i += Vector128<float>.Count)
            {
                var dx = Vector128.LoadUnsafe(ref _x[i]) - px;
                var dy = Vector128.LoadUnsafe(ref _y[i]) - py;
                var dz = Vector128.LoadUnsafe(ref _z[i]) - pz;
                var distanceSquared = (dx * dx + dy * dy) + dz * dz;
                var candidates = Vector128.LessThanOrEqual(distanceSquared, Vector128.LoadUnsafe(ref _rangeSquared[i]));
                AccumulateMask(candidates.ExtractMostSignificantBits(), i, position, normal, ref sum);
            }
        }

        for (; i < _lights.Length; i++)
        {
            Accumulate(i, position, normal, ref sum);
        }

        return new Color(Vector3.Clamp(sum, Vector3.Zero, Vector3.One));
    }

    private void AccumulateMask(uint mask, int start, Vector3 position, Vector3 normal, ref Vector3 sum)
    {
        // Ascending set bits preserve scalar accumulation order, including the colour quantization result.
        while (mask != 0)
        {
            var lane = BitOperations.TrailingZeroCount(mask);
            Accumulate(start + lane, position, normal, ref sum);
            mask &= mask - 1;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Accumulate(int index, Vector3 position, Vector3 normal, ref Vector3 sum)
    {
        var light = _lights[index];
        if (light.Range <= 0f)
        {
            return;
        }

        var toLight = light.Position - position;
        var distanceSquared = toLight.LengthSquared();
        var distance = MathF.Sqrt(distanceSquared);
        if (distance >= light.Range)
        {
            return;
        }

        var attenuation = 1f - distance / light.Range;
        attenuation *= attenuation;
        var cosine = distance > 1e-4f ? MathHelper.Clamp(Vector3.Dot(normal, toLight / distance), 0f, 1f) : 1f;
        sum += light.Color * (attenuation * (0.25f + 0.75f * cosine));
    }
}
