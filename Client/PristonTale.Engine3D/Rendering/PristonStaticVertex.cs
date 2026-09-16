using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

internal readonly struct PristonStaticVertex : IVertexType
{
    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(36, VertexElementFormat.Color, VertexElementUsage.Color, 1));

    public PristonStaticVertex(Vector3 position, Vector3 normal, Color color, Vector2 textureCoordinate, Color lampColor)
    {
        Position = position;
        Normal = normal;
        Color = color;
        TextureCoordinate = textureCoordinate;
        LampColor = lampColor;
    }

    public Vector3 Position { get; }
    public Vector3 Normal { get; }
    public Color Color { get; }
    public Vector2 TextureCoordinate { get; }
    public Color LampColor { get; }

    VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
}
