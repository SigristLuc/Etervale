using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

internal struct PristonSkinnedVertex : IVertexType
{
    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(36, VertexElementFormat.Single, VertexElementUsage.BlendIndices, 0));

    public PristonSkinnedVertex(Vector3 position, Vector3 normal, Color color, Vector2 texCoord, float boneIndex)
    {
        Position = position;
        Normal = normal;
        Color = color;
        TexCoord = texCoord;
        BoneIndex = boneIndex;
    }

    public Vector3 Position;
    public Vector3 Normal;
    public Color Color;
    public Vector2 TexCoord;
    public float BoneIndex;

    VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
}
