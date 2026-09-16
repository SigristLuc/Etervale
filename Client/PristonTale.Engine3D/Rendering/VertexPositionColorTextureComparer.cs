using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

internal sealed class VertexPositionColorTextureComparer : IEqualityComparer<VertexPositionColorTexture>
{
    public static readonly VertexPositionColorTextureComparer Instance = new();

    private VertexPositionColorTextureComparer()
    {
    }

    public bool Equals(VertexPositionColorTexture x, VertexPositionColorTexture y)
    {
        return x.Position.Equals(y.Position) &&
            x.Color.Equals(y.Color) &&
            x.TextureCoordinate.Equals(y.TextureCoordinate);
    }

    public int GetHashCode(VertexPositionColorTexture obj)
    {
        return HashCode.Combine(obj.Position, obj.Color, obj.TextureCoordinate);
    }
}
