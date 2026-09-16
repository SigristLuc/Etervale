using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Effects.Rendering;

public struct ParticleVertex : IVertexType
{
    public Vector3 Position;

    public Color Color;

    public Vector2 TexCoord;

    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(16, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

    public ParticleVertex(Vector3 position, Color color, Vector2 texCoord)
    {
        Position = position;
        Color = color;
        TexCoord = texCoord;
    }

    readonly VertexDeclaration IVertexType.VertexDeclaration => Declaration;
}
