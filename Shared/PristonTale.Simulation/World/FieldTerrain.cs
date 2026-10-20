#nullable enable
using System.Numerics;
using PristonTale.Geometry;

namespace PristonTale.Simulation.World;

// Authoritative ground for one field, from the same stage collision the client walks on. The original server loads
// every stage at startup and moves monsters with its floor-height query and next-move check.
// Without a mesh every query succeeds and keeps the caller's height, which is the behaviour before geometry existed.
public sealed class FieldTerrain
{
    // GroundTraversal on the client: at most 10 units of climb per step, walls probed 30 units above the feet. Players
    // and NPC walks use it; server-moved monsters climb MonsterStepping.StepUp like the original server.
    public const float StepUp = 10f;
    public const float BodyProbeHeight = 30f;
    // Drops settle on a floor within 32 units of the corpse, like the original stage item placement.
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

    // One probe of a body with the given half width, standing in for the body lines of the original next-move check: the
    // centre path leads by the half width and both flanks are swept too. A flank starts and ends short of the walls
    // found beside the body, and it may end closer to a wall than the half width only if it was already closer at
    // the start, so a body pressed against a wall can still slide along it or leave it.
    public bool TryBodyStep(Vector3 from, Vector3 to, float halfWidth, float stepUp, out Vector3 accepted)
    {
        accepted = from;
        if (_mesh is null)
        {
            accepted = to;
            return true;
        }

        var planar = new Vector3(to.X - from.X, 0f, to.Z - from.Z);
        var length = planar.Length();
        if (length < 0.0001f || !_mesh.TryGetFloorHeight(to.X, to.Z, from.Y, stepUp, out var height))
        {
            return false;
        }

        var direction = planar / length;
        var side = new Vector3(direction.Z, 0f, -direction.X);
        var end = to with { Y = from.Y };
        if (CrossesWall(from, end + direction * halfWidth) || FlankBlocked(from, end, side, halfWidth) || FlankBlocked(from, end, -side, halfWidth))
        {
            return false;
        }

        accepted = to with { Y = height };
        return true;
    }

    private bool FlankBlocked(Vector3 from, Vector3 end, Vector3 side, float halfWidth)
    {
        var atStart = WallClearance(from, side, halfWidth);
        var atEnd = WallClearance(end, side, halfWidth);
        if (atEnd < halfWidth && atEnd < atStart - 0.01f)
        {
            return true;
        }

        const float inset = 0.5f;
        return CrossesWall(from + side * MathF.Max(0f, atStart - inset), end + side * MathF.Max(0f, atEnd - inset));
    }

    private float WallClearance(Vector3 at, Vector3 direction, float limit)
    {
        var start = at + Vector3.UnitY * BodyProbeHeight;
        return _mesh!.TryTraceSegment(start, start + direction * limit, out var hit, out _) ? Vector3.Distance(start, hit) : limit;
    }

    // The client's wall slide (GameScene.MoveWithCollision) between two reported positions: stop just short of the
    // first wall, keep the motion along it and retry, three times at most. Faces as flat as a floor do not stop it.
    public Vector3 Slide(Vector3 from, Vector3 to)
    {
        if (_mesh is null)
        {
            return to with { Y = from.Y };
        }

        const float floorNormalY = 0.25f;
        const float clearance = 0.5f;
        var position = from;
        var remaining = new Vector3(to.X - from.X, 0f, to.Z - from.Z);
        var lift = Vector3.UnitY * BodyProbeHeight;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var length = remaining.Length();
            if (length < 0.0001f)
            {
                break;
            }

            if (!_mesh.TryTraceSegment(position + lift, position + lift + remaining, out var hit, out var normal) || MathF.Abs(normal.Y) >= floorNormalY)
            {
                position += remaining;
                break;
            }

            var direction = remaining / length;
            var allowed = Math.Clamp(Vector3.Distance(position + lift, hit) - clearance, 0f, length);
            position += direction * allowed;
            var wall = new Vector3(normal.X, 0f, normal.Z);
            if (wall.LengthSquared() < 1e-6f)
            {
                break;
            }

            wall = Vector3.Normalize(wall);
            var leftover = remaining - direction * allowed;
            remaining = leftover - wall * Vector3.Dot(leftover, wall);
        }

        return position;
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
