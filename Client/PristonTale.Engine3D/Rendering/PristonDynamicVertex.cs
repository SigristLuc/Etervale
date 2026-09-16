using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

internal struct PristonDynamicVertex : IVertexType
{
    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

    public PristonDynamicVertex(Vector3 position, Vector3 normal, Color color, Vector2 textureCoordinate)
    {
        Position = position;
        Normal = normal;
        Color = color;
        TextureCoordinate = textureCoordinate;
    }

    public Vector3 Position;
    public Vector3 Normal;
    public Color Color;
    public Vector2 TextureCoordinate;

    VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
}

internal static class PristonVertexNormals
{
    public static void RecalculateTriangleNormals(PristonDynamicVertex[] vertices)
    {
        for (var i = 0; i + 2 < vertices.Length; i += 3)
        {
            var normal = Vector3.Cross(
                vertices[i + 1].Position - vertices[i].Position,
                vertices[i + 2].Position - vertices[i].Position);
            if (normal.LengthSquared() < 0.000001f)
                normal = Vector3.Up;
            else
                normal.Normalize();

            vertices[i].Normal = normal;
            vertices[i + 1].Normal = normal;
            vertices[i + 2].Normal = normal;
        }
    }
}
