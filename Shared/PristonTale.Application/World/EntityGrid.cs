using System.Numerics;

namespace PristonTale.Application.World;

// Uniform planar buckets over one field snapshot. Entity indices are sorted by cell key so a query is a binary
// search per overlapped cell; the original server instead scanned every connection and monster slot.
public sealed class EntityGrid
{
    public const float CellSize = 256f;
    private readonly long[] _keys;
    private readonly int[] _indices;
    private readonly Dictionary<uint, int> _byId;

    private EntityGrid(long[] keys, int[] indices, Dictionary<uint, int> byId)
    {
        _keys = keys;
        _indices = indices;
        _byId = byId;
    }

    public static EntityGrid Build(EntityView[] entities)
    {
        var keys = new long[entities.Length];
        var indices = new int[entities.Length];
        var byId = new Dictionary<uint, int>(entities.Length);
        for (var index = 0; index < entities.Length; index++)
        {
            keys[index] = KeyOf(entities[index].Position);
            indices[index] = index;
            byId[entities[index].EntityId] = index;
        }

        Array.Sort(keys, indices);
        return new EntityGrid(keys, indices, byId);
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
            for (var z = minZ; z <= maxZ; z++)
            {
                var key = Key(x, z);
                var position = LowerBound(key);
                while (position < _keys.Length && _keys[position] == key)
                {
                    results.Add(_indices[position]);
                    position++;
                }
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

    private static long Key(int x, int z) => (long)x << 32 | (uint)z;

    private static long KeyOf(Vector3 position) => Key(Cell(position.X), Cell(position.Z));
}
