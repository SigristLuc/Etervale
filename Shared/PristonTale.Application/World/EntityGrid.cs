using System.Numerics;

namespace PristonTale.Application.World;

// Uniform planar buckets over one field snapshot. Entity indices are sorted by cell key so a query is a binary
// search per overlapped cell; the original server instead scanned every connection and monster slot. A field's
// presence frames rebuild their grid in place every tick.
public sealed class EntityGrid
{
    public const float CellSize = 256f;
    private long[] _keys;
    private int[] _indices;
    private readonly Dictionary<uint, int> _byId;

    private EntityGrid(int capacity)
    {
        _keys = new long[capacity];
        _indices = new int[capacity];
        _byId = new Dictionary<uint, int>(capacity);
    }

    public static EntityGrid Build(EntityView[] entities)
    {
        var grid = new EntityGrid(entities.Length);
        grid.Rebuild(entities);
        return grid;
    }

    public void Rebuild(EntityView[] entities)
    {
        if (_keys.Length != entities.Length)
        {
            _keys = new long[entities.Length];
            _indices = new int[entities.Length];
        }

        _byId.Clear();
        _byId.EnsureCapacity(entities.Length);
        for (var index = 0; index < entities.Length; index++)
        {
            _keys[index] = KeyOf(entities[index].Position);
            _indices[index] = index;
            _byId[entities[index].EntityId] = index;
        }

        Array.Sort(_keys, _indices);
    }

    public bool TryGetIndex(uint entityId, out int index) => _byId.TryGetValue(entityId, out index);

    // Appends indices of entities in cells overlapping the square around center; callers apply the exact radius.
    public void Query(Vector3 center, float radius, List<int> results)
    {
        var minX = Cell(center.X - radius);
        var maxX = Cell(center.X + radius);
        var minZ = Cell(center.Z - radius);
        var maxZ = Cell(center.Z + radius);
        for (var x = minX; x <= maxX; x++)
        {
            // Cells of one column are adjacent in key order: one search finds the column's first cell in range.
            var position = LowerBound(Key(x, minZ));
            var last = Key(x, maxZ);
            while (position < _keys.Length && _keys[position] <= last)
            {
                results.Add(_indices[position]);
                position++;
            }
        }
    }

    private int LowerBound(long key)
    {
        int low = 0;
        int high = _keys.Length;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (_keys[middle] < key)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static int Cell(float coordinate) => (int)MathF.Floor(coordinate / CellSize);

    // The sign flip keeps keys in coordinate order, so the cells of a column are contiguous across zero.
    private static long Key(int x, int z) => (long)x << 32 | (uint)(z ^ int.MinValue);

    private static long KeyOf(Vector3 position) => Key(Cell(position.X), Cell(position.Z));
}
