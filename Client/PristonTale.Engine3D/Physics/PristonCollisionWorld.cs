using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Physics;

public sealed class PristonCollisionWorld : IPristonCollisionQuery
{
    private const uint CheckFace = 0x00000001;

    // Floor-normal Y component must exceed this for a triangle to be considered walkable.
    private const float MinFloorNormalY = 0.25f;

    // Immutable triangle backing stores used by every query after construction.
    private readonly FloorTriangle[] _floorTriangles;
    private readonly CollisionTriangle[] _collisionTriangles;

    // The XZ grid targets eight triangles per cell and registers every cell overlapped by each AABB.
    // Query stamps deduplicate multi-cell triangles without per-query allocation.

    private readonly int[][] _floorGrid;
    private readonly int[][] _collisionGrid;

    private readonly float _gridMinX;
    private readonly float _gridMinZ;
    private readonly float _cellSize;
    private readonly int _gridCols;
    private readonly int _gridRows;

    // Dedup stamps — sized to triangle count, value = last _queryId that touched index.
    private readonly int[] _floorVisited;
    private readonly int[] _collisionVisited;
    private int _queryId;

    public PristonCollisionWorld(PristonStage stage)
    {
        var floorTriangles = new List<FloorTriangle>();
        var collisionTriangles = new List<CollisionTriangle>();

        foreach (var face in stage.Faces)
        {
            if (face.A < 0 || face.B < 0 || face.C < 0 ||
                face.A >= stage.Vertices.Count ||
                face.B >= stage.Vertices.Count ||
                face.C >= stage.Vertices.Count)
                continue;

            if (face.MaterialIndex < 0 ||
                face.MaterialIndex >= stage.Materials.Count ||
                (stage.Materials[face.MaterialIndex].MeshState & CheckFace) == 0)
                continue;

            var a = stage.Vertices[face.A].Position;
            var b = stage.Vertices[face.B].Position;
            var c = stage.Vertices[face.C].Position;
            collisionTriangles.Add(new CollisionTriangle(a, b, c));

            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() < 0.0001f)
                continue;

            normal.Normalize();
            if (MathF.Abs(normal.Y) < MinFloorNormalY)
                continue;

            floorTriangles.Add(new FloorTriangle(a, b, c));
        }

        _floorTriangles = floorTriangles.ToArray();
        _collisionTriangles = collisionTriangles.ToArray();

        var minX = float.MaxValue;
        var minZ = float.MaxValue;
        var maxX = float.MinValue;
        var maxZ = float.MinValue;

        for (var i = 0; i < _collisionTriangles.Length; i++)
        {
            _collisionTriangles[i].GetXZBounds(out var triMinX, out var triMinZ, out var triMaxX, out var triMaxZ);
            if (triMinX < minX) minX = triMinX;
            if (triMinZ < minZ) minZ = triMinZ;
            if (triMaxX > maxX) maxX = triMaxX;
            if (triMaxZ > maxZ) maxZ = triMaxZ;
        }

        if (maxX <= minX || maxZ <= minZ || (_collisionTriangles.Length == 0 && _floorTriangles.Length == 0))
        {
            minX = 0f;
            minZ = 0f;
            maxX = 1f;
            maxZ = 1f;
        }

        int totalTris = Math.Max(_collisionTriangles.Length, 1);
        float wx = maxX - minX;
        float wz = maxZ - minZ;
        float worldArea = wx * wz;
        float rawCell = MathF.Sqrt(worldArea * 8f / totalTris);
        float cellSize = Math.Clamp(rawCell, 32f, 512f);

        // Expand grid bounds by one full cell on each side so boundary triangles
        // land cleanly inside a cell rather than exactly on an edge.
        _gridMinX = minX - cellSize;
        _gridMinZ = minZ - cellSize;
        _cellSize = cellSize;

        float expandedW = (maxX + cellSize) - _gridMinX;
        float expandedH = (maxZ + cellSize) - _gridMinZ;

        _gridCols = Math.Max(1, (int)MathF.Ceiling(expandedW / cellSize));
        _gridRows = Math.Max(1, (int)MathF.Ceiling(expandedH / cellSize));

        int totalCells = _gridCols * _gridRows;

        var floorCells = new List<int>[totalCells];
        var collisionCells = new List<int>[totalCells];
        for (var i = 0; i < _floorTriangles.Length; i++)
        {
            _floorTriangles[i].GetXZBounds(out var triMinX, out var triMinZ, out var triMaxX, out var triMaxZ);
            int c0 = WorldToCol(triMinX);
            int r0 = WorldToRow(triMinZ);
            int c1 = WorldToCol(triMaxX);
            int r1 = WorldToRow(triMaxZ);
            for (var r = r0; r <= r1; r++)
            {
                for (var c = c0; c <= c1; c++)
                {
                    var cellIndex = r * _gridCols + c;
                    (floorCells[cellIndex] ??= new List<int>(8)).Add(i);
                }
            }
        }

        for (var i = 0; i < _collisionTriangles.Length; i++)
        {
            _collisionTriangles[i].GetXZBounds(out var triMinX, out var triMinZ, out var triMaxX, out var triMaxZ);
            int c0 = WorldToCol(triMinX);
            int r0 = WorldToRow(triMinZ);
            int c1 = WorldToCol(triMaxX);
            int r1 = WorldToRow(triMaxZ);
            for (var r = r0; r <= r1; r++)
            {
                for (var c = c0; c <= c1; c++)
                {
                    var cellIndex = r * _gridCols + c;
                    (collisionCells[cellIndex] ??= new List<int>(8)).Add(i);
                }
            }
        }

        _floorGrid = new int[totalCells][];
        _collisionGrid = new int[totalCells][];
        for (var i = 0; i < totalCells; i++)
        {
            _floorGrid[i] = floorCells[i]?.ToArray() ?? [];
            _collisionGrid[i] = collisionCells[i]?.ToArray() ?? [];
        }

        _floorVisited = new int[_floorTriangles.Length];
        _collisionVisited = new int[_collisionTriangles.Length];
        _queryId = 0;
    }

    private int WorldToCol(float x)
        => Math.Clamp((int)MathF.Floor((x - _gridMinX) / _cellSize), 0, _gridCols - 1);

    private int WorldToRow(float z)
        => Math.Clamp((int)MathF.Floor((z - _gridMinZ) / _cellSize), 0, _gridRows - 1);

    private int CellIndex(int col, int row) => row * _gridCols + col;

    public bool TryGetFloorHeight(Vector3 position, out float height)
    {
        int col = WorldToCol(position.X);
        int row = WorldToRow(position.Z);
        var cell = _floorGrid[CellIndex(col, row)];

        var found = false;
        var best = float.MinValue;
        for (var i = 0; i < cell.Length; i++)
        {
            if (!_floorTriangles[cell[i]].TryGetHeight(position.X, position.Z, out var triangleHeight))
                continue;

            if (!found || triangleHeight > best)
            {
                best = triangleHeight;
                found = true;
            }
        }

        height = found ? best : 0f;
        return found;
    }

    public bool TryGetFloorHeight(Vector3 position, float referenceHeight, float stepUp, out float height)
    {
        int col = WorldToCol(position.X);
        int row = WorldToRow(position.Z);
        var cell = _floorGrid[CellIndex(col, row)];

        var found = false;
        var best = float.MinValue;
        for (var i = 0; i < cell.Length; i++)
        {
            if (!_floorTriangles[cell[i]].TryGetHeight(position.X, position.Z, out var h))
                continue;
            if (h > referenceHeight + stepUp)
                continue; // floor overhead beyond a step (ceiling / bridge deck) — not reachable from here

            if (!found || h > best)
            {
                best = h;
                found = true;
            }
        }

        height = found ? best : 0f;
        return found;
    }

    public Vector3 PlaceOnFloor(Vector3 position)
    {
        return TryGetFloorHeight(position, out var height)
            ? new Vector3(position.X, height, position.Z)
            : position;
    }

    public bool TryRaycastFloor(Ray ray, out Vector3 point)
    {
        _queryId++;
        var qid = _queryId;

        var visitor = new FloorRaycastVisitor(this, ray, qid);

        // Walk XZ-projected DDA; for each visited cell test floor triangles.
        TraverseGrid2D(
            ray.Position.X, ray.Position.Z,
            ray.Direction.X, ray.Direction.Z,
            float.MaxValue,
            ref visitor);

        point = visitor.BestPoint;
        return visitor.Found;
    }

    public bool TryTraceSegment(Vector3 start, Vector3 end, out Vector3 hitPoint)
        => TryTraceSegment(start, end, out hitPoint, out _);

    public bool TryTraceSegment(Vector3 start, Vector3 end, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        var direction = end - start;
        var length = direction.Length();
        if (length < 0.0001f)
        {
            hitPoint = start;
            hitNormal = Vector3.Zero;
            return false;
        }

        direction /= length;
        var ray = new Ray(start, direction);

        _queryId++;
        var qid = _queryId;

        var visitor = new SegmentTraceVisitor(this, ray, qid, length);

        TraverseGrid2D(
            start.X, start.Z,
            direction.X, direction.Z,
            length,
            ref visitor);

        hitPoint = visitor.BestPoint;
        hitNormal = visitor.BestNormal;
        return visitor.Found;
    }

    // XZ DDA uses a struct visitor to avoid boxing; vertical rays visit only their starting cell.
    private void TraverseGrid2D<TVisitor>(
        float ox, float oz,
        float dx, float dz,
        float maxT,
        ref TVisitor visitor)
        where TVisitor : struct, IGridCellVisitor
    {
        int col = WorldToCol(ox);
        int row = WorldToRow(oz);

        if (MathF.Abs(dx) < 1e-9f && MathF.Abs(dz) < 1e-9f)
        {
            visitor.VisitCell(col, row);
            return;
        }

        int stepCol = dx >= 0f ? 1 : -1;
        int stepRow = dz >= 0f ? 1 : -1;

        float cellLeft = _gridMinX + col * _cellSize;
        float cellRight = _gridMinX + (col + 1) * _cellSize;
        float cellBottom = _gridMinZ + row * _cellSize;
        float cellTop = _gridMinZ + (row + 1) * _cellSize;

        float tMaxX = MathF.Abs(dx) > 1e-9f
            ? ((dx > 0f ? cellRight : cellLeft) - ox) / dx
            : float.MaxValue;
        float tMaxZ = MathF.Abs(dz) > 1e-9f
            ? ((dz > 0f ? cellTop : cellBottom) - oz) / dz
            : float.MaxValue;

        float tDeltaX = MathF.Abs(dx) > 1e-9f ? _cellSize / MathF.Abs(dx) : float.MaxValue;
        float tDeltaZ = MathF.Abs(dz) > 1e-9f ? _cellSize / MathF.Abs(dz) : float.MaxValue;

        while (true)
        {
            if (col < 0 || col >= _gridCols || row < 0 || row >= _gridRows)
                break;

            visitor.VisitCell(col, row);

            if (tMaxX < tMaxZ)
            {
                if (tMaxX > maxT) break;
                col += stepCol;
                tMaxX += tDeltaX;
            }
            else
            {
                if (tMaxZ > maxT) break;
                row += stepRow;
                tMaxZ += tDeltaZ;
            }
        }
    }

    private interface IGridCellVisitor
    {
        void VisitCell(int col, int row);
    }

    private struct FloorRaycastVisitor : IGridCellVisitor
    {
        private readonly PristonCollisionWorld _world;
        private readonly Ray _ray;
        private readonly int _queryId;
        private float _bestDistance;

        public FloorRaycastVisitor(PristonCollisionWorld world, Ray ray, int queryId)
        {
            _world = world;
            _ray = ray;
            _queryId = queryId;
            _bestDistance = float.MaxValue;
            BestPoint = Vector3.Zero;
            Found = false;
        }

        public Vector3 BestPoint { get; private set; }
        public bool Found { get; private set; }

        public void VisitCell(int col, int row)
        {
            var cell = _world._floorGrid[_world.CellIndex(col, row)];
            for (var index = 0; index < cell.Length; index++)
            {
                var triangleIndex = cell[index];
                if (_world._floorVisited[triangleIndex] == _queryId)
                {
                    continue;
                }

                _world._floorVisited[triangleIndex] = _queryId;
                if (!_world._floorTriangles[triangleIndex].TryRaycast(_ray, out var distance)
                    || distance < 0f
                    || distance >= _bestDistance)
                {
                    continue;
                }

                _bestDistance = distance;
                BestPoint = _ray.Position + _ray.Direction * distance;
                Found = true;
            }
        }
    }

    private struct SegmentTraceVisitor : IGridCellVisitor
    {
        private readonly PristonCollisionWorld _world;
        private readonly Ray _ray;
        private readonly int _queryId;
        private float _bestDistance;

        public SegmentTraceVisitor(PristonCollisionWorld world, Ray ray, int queryId, float maximumDistance)
        {
            _world = world;
            _ray = ray;
            _queryId = queryId;
            _bestDistance = maximumDistance;
            BestPoint = Vector3.Zero;
            BestNormal = Vector3.Zero;
            Found = false;
        }

        public Vector3 BestPoint { get; private set; }
        public Vector3 BestNormal { get; private set; }
        public bool Found { get; private set; }

        public void VisitCell(int col, int row)
        {
            var cell = _world._collisionGrid[_world.CellIndex(col, row)];
            for (var index = 0; index < cell.Length; index++)
            {
                var triangleIndex = cell[index];
                if (_world._collisionVisited[triangleIndex] == _queryId)
                {
                    continue;
                }

                _world._collisionVisited[triangleIndex] = _queryId;
                if (!_world._collisionTriangles[triangleIndex].TryRaycast(_ray, out var distance)
                    || distance < 0f
                    || distance >= _bestDistance)
                {
                    continue;
                }

                _bestDistance = distance;
                BestPoint = _ray.Position + _ray.Direction * distance;
                BestNormal = _world._collisionTriangles[triangleIndex].Normal;
                Found = true;
            }
        }
    }

    private readonly struct FloorTriangle
    {
        private readonly Vector3 _a;
        private readonly Vector3 _b;
        private readonly Vector3 _c;
        private readonly float _denominator;

        public FloorTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            _a = a;
            _b = b;
            _c = c;
            _denominator =
                ((_b.Z - _c.Z) * (_a.X - _c.X)) +
                ((_c.X - _b.X) * (_a.Z - _c.Z));
        }

        public void GetXZBounds(out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = MathF.Min(_a.X, MathF.Min(_b.X, _c.X));
            maxX = MathF.Max(_a.X, MathF.Max(_b.X, _c.X));
            minZ = MathF.Min(_a.Z, MathF.Min(_b.Z, _c.Z));
            maxZ = MathF.Max(_a.Z, MathF.Max(_b.Z, _c.Z));
        }

        public bool TryGetHeight(float x, float z, out float height)
        {
            if (MathF.Abs(_denominator) < 0.0001f)
            {
                height = 0f;
                return false;
            }

            var u =
                (((_b.Z - _c.Z) * (x - _c.X)) +
                 ((_c.X - _b.X) * (z - _c.Z))) /
                _denominator;
            var v =
                (((_c.Z - _a.Z) * (x - _c.X)) +
                 ((_a.X - _c.X) * (z - _c.Z))) /
                _denominator;
            var w = 1f - u - v;
            if (u < -0.001f || v < -0.001f || w < -0.001f)
            {
                height = 0f;
                return false;
            }

            height = (_a.Y * u) + (_b.Y * v) + (_c.Y * w);
            return true;
        }

        public bool TryRaycast(Ray ray, out float distance)
        {
            return CollisionTriangle.Raycast(_a, _b, _c, ray, out distance);
        }
    }

    private readonly struct CollisionTriangle
    {
        private readonly Vector3 _a;
        private readonly Vector3 _b;
        private readonly Vector3 _c;

        // Geometric (un-oriented) face normal, computed once. Used by the segment trace so callers
        // (player wall slide) can project against the surface; sign depends on winding, so consumers
        // that need an oriented normal must flip it against their motion.
        public Vector3 Normal { get; }

        public CollisionTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            _a = a;
            _b = b;
            _c = c;
            var cross = Vector3.Cross(b - a, c - a);
            Normal = cross.LengthSquared() > 1e-12f ? Vector3.Normalize(cross) : Vector3.Up;
        }

        public void GetXZBounds(out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = MathF.Min(_a.X, MathF.Min(_b.X, _c.X));
            maxX = MathF.Max(_a.X, MathF.Max(_b.X, _c.X));
            minZ = MathF.Min(_a.Z, MathF.Min(_b.Z, _c.Z));
            maxZ = MathF.Max(_a.Z, MathF.Max(_b.Z, _c.Z));
        }

        public bool TryRaycast(Ray ray, out float distance)
        {
            return Raycast(_a, _b, _c, ray, out distance);
        }

        public static bool Raycast(Vector3 a, Vector3 b, Vector3 c, Ray ray, out float distance)
        {
            var edge1 = b - a;
            var edge2 = c - a;
            var h = Vector3.Cross(ray.Direction, edge2);
            var determinant = Vector3.Dot(edge1, h);
            if (MathF.Abs(determinant) < 0.000001f)
            {
                distance = 0f;
                return false;
            }

            var invDeterminant = 1f / determinant;
            var s = ray.Position - a;
            var u = invDeterminant * Vector3.Dot(s, h);
            if (u < 0f || u > 1f)
            {
                distance = 0f;
                return false;
            }

            var q = Vector3.Cross(s, edge1);
            var v = invDeterminant * Vector3.Dot(ray.Direction, q);
            if (v < 0f || u + v > 1f)
            {
                distance = 0f;
                return false;
            }

            distance = invDeterminant * Vector3.Dot(edge2, q);
            return distance >= 0f;
        }
    }
}
