using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Geometry;
using NumVector3 = System.Numerics.Vector3;

namespace PristonTale.Engine3D.Physics;

// MonoGame-space view of the shared CollisionMesh, which the server also builds from the same stage files, with the
// stage's ice floors (solid faces of an ice material) and water surfaces (faces of a water or see-through material that
// do not collide) for the marks a step leaves.
public sealed class PristonCollisionWorld : IPristonCollisionQuery
{
    // A material more transparent than this is water to the steps, as the original's stages mark it.
    public const float WaterTransparency = 0.1f;

    private readonly CollisionMesh _mesh;
    private readonly CollisionMesh _ice;
    private readonly CollisionMesh _water;

    public PristonCollisionWorld(PristonStage stage)
    {
        var triangles = new List<Triangle>(stage.Faces.Count);
        var ice = new List<Triangle>();
        var water = new List<Triangle>();
        foreach (var face in stage.Faces)
        {
            if (face.A < 0 || face.B < 0 || face.C < 0 || face.A >= stage.Vertices.Count || face.B >= stage.Vertices.Count
                || face.C >= stage.Vertices.Count || face.MaterialIndex < 0 || face.MaterialIndex >= stage.Materials.Count)
            {
                continue;
            }

            var material = stage.Materials[face.MaterialIndex];
            var triangle = new Triangle(ToNumerics(stage.Vertices[face.A].Position), ToNumerics(stage.Vertices[face.B].Position),
                ToNumerics(stage.Vertices[face.C].Position));
            if ((material.MeshState & StageCollisionReader.CheckFace) == 0)
            {
                if (material.IsWater || material.Transparency > WaterTransparency)
                {
                    water.Add(triangle);
                }

                continue;
            }

            triangles.Add(triangle);
            if ((material.SurfaceFlags & PristonMaterialSurfaceFlags.Ice) != 0)
            {
                ice.Add(triangle);
            }
        }

        _mesh = new CollisionMesh(triangles);
        _ice = new CollisionMesh(ice);
        _water = new CollisionMesh(water);
    }

    // The highest ice floor under a point no more than reach above it, with the floor's normal.
    public bool TryGetIceFloor(Vector3 position, float reach, out Vector3 point, out Vector3 normal)
    {
        var found = _ice.TryTraceSegment(ToNumerics(position + Vector3.Up * reach), ToNumerics(position - Vector3.Up * 4096f), out var hit,
            out var faceNormal);
        point = ToXna(hit);
        normal = ToXna(faceNormal);
        if (normal.Y < 0f)
        {
            normal = -normal;
        }

        return found;
    }

    // The highest water surface over the point's column.
    public bool TryGetWaterHeight(Vector3 position, out float height) => _water.TryGetFloorHeight(position.X, position.Z, out height);

    public bool TryGetFloorHeight(Vector3 position, out float height) => _mesh.TryGetFloorHeight(position.X, position.Z, out height);

    public bool TryGetFloorHeight(Vector3 position, float referenceHeight, float stepUp, out float height) =>
        _mesh.TryGetFloorHeight(position.X, position.Z, referenceHeight, stepUp, out height);

    public Vector3 PlaceOnFloor(Vector3 position)
    {
        return TryGetFloorHeight(position, out var height)
            ? new Vector3(position.X, height, position.Z)
            : position;
    }

    public bool TryRaycastFloor(Ray ray, out Vector3 point)
    {
        var found = _mesh.TryRaycastFloor(ToNumerics(ray.Position), ToNumerics(ray.Direction), out var hit);
        point = ToXna(hit);
        return found;
    }

    public bool TryTraceSegment(Vector3 start, Vector3 end, out Vector3 hitPoint)
        => TryTraceSegment(start, end, out hitPoint, out _);

    public bool TryTraceSegment(Vector3 start, Vector3 end, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        var found = _mesh.TryTraceSegment(ToNumerics(start), ToNumerics(end), out var point, out var normal);
        hitPoint = ToXna(point);
        hitNormal = ToXna(normal);
        return found;
    }

    private static NumVector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);

    private static Vector3 ToXna(NumVector3 value) => new(value.X, value.Y, value.Z);
}
