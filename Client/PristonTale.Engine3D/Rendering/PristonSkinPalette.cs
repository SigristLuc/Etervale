using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering;

internal sealed class PristonSkinPalette
{
    public PristonSkinPalette(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, PristonEffectPipeline.MaxBones);
        Matrices = new Matrix[count];
        ShaderRows = new Vector4[count * 3];
    }

    public Matrix[] Matrices { get; }
    public Vector4[] ShaderRows { get; }
    public int Count => Matrices.Length;
    public uint Revision { get; private set; }

    public void Pack()
    {
        for (var bone = 0; bone < Matrices.Length; bone++)
        {
            var matrix = Matrices[bone];
            var row = bone * 3;
            ShaderRows[row] = new Vector4(matrix.M11, matrix.M21, matrix.M31, matrix.M41);
            ShaderRows[row + 1] = new Vector4(matrix.M12, matrix.M22, matrix.M32, matrix.M42);
            ShaderRows[row + 2] = new Vector4(matrix.M13, matrix.M23, matrix.M33, matrix.M43);
        }

        Revision++;
    }
}
