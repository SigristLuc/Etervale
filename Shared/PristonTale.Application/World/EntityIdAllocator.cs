using System.Threading;

namespace PristonTale.Application.World;

public sealed class EntityIdAllocator
{
    public const uint MonsterIdBase = 0x4000_0000u;

    private long _next = MonsterIdBase - 1;

    public uint NextMonsterId() => (uint)Interlocked.Increment(ref _next);
}
