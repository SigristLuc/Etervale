#nullable enable
using System.Numerics;
using PristonTale.Geometry;

namespace PristonTale.Simulation.World;

// Authoritative ground for one field, from the same stage collision the client walks on. The original server loads
// every stage at startup (OnSever.cpp srReadStage) and moves monsters with GetFloorHeight and CheckNextMove.
// Without a mesh every query succeeds and keeps the caller's height, which is the behaviour before geometry existed.
public sealed class FieldTerrain
{
    // GroundTraversal on the client: at most 10 units of climb per step, walls probed 30 units above the feet.
    public const float StepUp = 10f;
    public const float BodyProbeHeight = 30f;
    // Drops settle on a floor within 32 units of the corpse, like STG_AREA::AddItem.
    public const float DropTolerance = 32f;

    public static readonly FieldTerrain None = new(null);

    private readonly CollisionMesh? _mesh;

    public FieldTerrain(CollisionMesh? mesh)
    {
        _mesh = mesh;
    }

    public bool HasGeometry => _mesh is not null;

    // Highest walkable surface under the point, for spawns, start points and respawns.
    public Vector3 PlaceOnFloor(Vector3 position)
    {
        return _mesh is not null && _mesh.TryGetFloorHeight(position.X, position.Z, out var height)
            ? position with { Y = height }
            : position;
    }

    public bool TryFloorNear(Vector3 position, float climb, out float height)
    {
        if (_mesh is null)
        {
            height = position.Y;
            return true;
        }

        return _mesh.TryGetFloorHeight(position.X, position.Z, position.Y, climb, out height);
    }

    public Vector3 SettleNear(Vector3 position, float tolerance)
    {
        return TryFloorNear(position, tolerance, out var height) && position.Y - height <= tolerance
            ? position with { Y = height }
            : position;
    }

    public bool CrossesWall(Vector3 from, Vector3 to)
    {
        if (_mesh is null)
        {
            return false;
        }

        var lift = Vector3.UnitY * BodyProbeHeight;
        return _mesh.TryTraceSegment(from + lift, to with { Y = from.Y } + lift, out _, out _);
    }

    // One movement step for a walking entity: blocked by walls and by climbs above StepUp; falls are allowed.
    public bool TryStep(Vector3 from, Vector3 to, out Vector3 accepted)
    {
        accepted = from;
        if (_mesh is null)
        {
            accepted = to;
            return true;
        }

        if (CrossesWall(from, to) || !_mesh.TryGetFloorHeight(to.X, to.Z, from.Y, StepUp, out var height))
        {
            return false;
        }

        accepted = to with { Y = height };
        return true;
    }
}
