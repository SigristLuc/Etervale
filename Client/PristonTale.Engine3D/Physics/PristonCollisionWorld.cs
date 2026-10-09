using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Geometry;
using NumVector3 = System.Numerics.Vector3;

namespace PristonTale.Engine3D.Physics;

// MonoGame-space view of the shared CollisionMesh, which the server also builds from the same stage files.
public sealed class PristonCollisionWorld : IPristonCollisionQuery
{
    private readonly CollisionMesh _mesh;

    public PristonCollisionWorld(PristonStage stage)
    {
        var triangles = new List<Triangle>(stage.Faces.Count);
        foreach (var face in stage.Faces)
        {
            if (face.A < 0 || face.B < 0 || face.C < 0 || face.A >= stage.Vertices.Count || face.B >= stage.Vertices.Count
                || face.C >= stage.Vertices.Count || face.MaterialIndex < 0 || face.MaterialIndex >= stage.Materials.Count
                || (stage.Materials[face.MaterialIndex].MeshState & StageCollisionReader.CheckFace) == 0)
            {
                continue;
            }

            triangles.Add(new Triangle(ToNumerics(stage.Vertices[face.A].Position), ToNumerics(stage.Vertices[face.B].Position),
                ToNumerics(stage.Vertices[face.C].Position)));
        }

        _mesh = new CollisionMesh(triangles);
    }

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
