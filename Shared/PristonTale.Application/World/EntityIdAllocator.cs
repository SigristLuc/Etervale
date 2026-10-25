namespace PristonTale.Application.World;

// Monster, summon and NPC ids: 32-bit, from MonsterIdBase up, shared by every field. Removed monsters release their
// ids; once the counter reaches the top of the range it starts again at the base and skips ids still in use, so a
// long-running server never hands out a live id or wraps into player ids.
public sealed class EntityIdAllocator
{
    public const uint MonsterIdBase = 0x4000_0000u;

    private readonly object _gate = new();
    private readonly HashSet<uint> _live = new();
    private uint _last = MonsterIdBase - 1;

    public int LiveCount
    {
        get
        {
            lock (_gate)
            {
                return _live.Count;
            }
        }
    }

    public uint NextMonsterId()
    {
        // The range holds over three billion ids, far more than a world can keep alive at once, so the search ends.
        lock (_gate)
        {
            do
            {
                _last = _last is uint.MaxValue or < MonsterIdBase - 1 ? MonsterIdBase : _last + 1;
            }
            while (!_live.Add(_last));

            return _last;
        }
    }

    public void Release(uint id)
    {
        lock (_gate)
        {
            _live.Remove(id);
        }
    }

    // The next id handed out follows `last`; tests move the counter to the top of the range.
    internal void Rewind(uint last)
    {
        lock (_gate)
        {
            _last = last;
        }
    }
}
