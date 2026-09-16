using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

internal sealed record RenderBatch(
    PristonDynamicVertex[] Vertices,
    PristonDynamicVertex[] BaseVertices,
    PristonGpuMesh Mesh,
    PristonMaterialRuntime Material,
    bool ForceCastsShadow = false,
    bool ShadowCoveredByAggregate = false,
    bool IsSkinned = false,
    VertexBuffer SkinnedBuffer = null,
    IndexBuffer SkinnedIndexBuffer = null,
    int SkinnedPrimitiveCount = 0,
    int SkinnedVertexCount = 0,
    int SkinnedIndexCount = 0,
    BoundingBox SkinnedBounds = default,
    int BoneCount = 0)
{
    // For non-skinned batches use the static mesh bounds or compute from vertices.
    // For skinned batches use SkinnedBounds (bind-pose AABB — may be slightly off
    // for animated geometry; frustum culling will be conservative).
    public BoundingBox Bounds { get; private set; } =
        IsSkinned ? SkinnedBounds :
        (Mesh?.Bounds ?? CalculateBounds(Vertices));

    public int TriangleCount => IsSkinned ? SkinnedPrimitiveCount : (Mesh?.PrimitiveCount ?? Vertices.Length / 3);

    public PristonSkinPalette SkinPalette { get; private set; }
    public Matrix[] BonePalette => SkinPalette?.Matrices ?? [];

    public DynamicVertexBuffer DynamicBuffer { get; set; }
    public bool DynamicBufferDirty { get; set; }

    public Vector3 ActorColorAdd { get; set; }

    public void UseSkinPalette(PristonSkinPalette palette)
    {
        if (!IsSkinned || palette.Count != BoneCount)
        {
            throw new ArgumentException("A skinned batch must use its actor's matching compact palette.", nameof(palette));
        }

        SkinPalette = palette;
    }

    public IEnumerable<Vector3> BoundsPoints()
    {
        return Bounds.GetCorners();
    }

    public void RefreshBounds()
    {
        if (Mesh == null && !IsSkinned)
            Bounds = CalculateBounds(Vertices);
    }

    public void SetWorldBounds(BoundingBox bounds) => Bounds = bounds;

    private static BoundingBox CalculateBounds(PristonDynamicVertex[] vertices)
    {
        if (vertices.Length == 0)
            return new BoundingBox(Vector3.Zero, Vector3.Zero);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < vertices.Length; i++)
        {
            var p = vertices[i].Position;
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return new BoundingBox(min, max);
    }
}

internal readonly record struct SkinnedVertKey(Vector3 Position, Vector3 Normal, Vector2 TexCoord, float BoneIndex);

internal sealed record ModelTriangleRef(int ObjectIndex, PristonModelFace Face);

internal sealed record ActorTriangleRef(int ObjectIndex, PristonModelFace Face, int ABoneIndex, int BBoneIndex, int CBoneIndex, Matrix ObjectTransform);
