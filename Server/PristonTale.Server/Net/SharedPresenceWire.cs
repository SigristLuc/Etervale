using PristonTale.Application.World;
using PristonTale.Protocol.Packets;

namespace PristonTale.Server.Net;

// The play-data entries of one presence frame, encoded once for every receiver of the frame. An entity's entry depends
// only on its view, the field and its field-wide descriptor revision, so every encoder copies the same bytes; the first
// encoder that needs an entry writes it while the others wait the few nanoseconds it takes.
internal sealed class SharedPresenceWire : IPresenceFrameAttachment
{
    private static readonly int DetailedSize = TransPlayData.SizeFor(1);
    private const int CoarseSize = PlayDataCoarse.Size;
    private const int DetailedBusy = 1;
    private const int DetailedDone = 2;
    private const int CoarseBusy = 4;
    private const int CoarseDone = 8;
    private const int DescriptionBusy = 16;
    private const int DescriptionDone = 32;
    private byte[] _detailed = [];
    private byte[] _coarse = [];
    private byte[]? _described;
    private int[] _state = [];

    private SharedPresenceWire(int entityCount) => Reset(entityCount);

    public static SharedPresenceWire Create(int entityCount) => new(entityCount);

    // Field tick only, while no encoder holds the frame.
    // Buffers keep their capacity (with slack) across ticks so a field whose entity count moves does not reallocate them.
    public void Reset(int entityCount)
    {
        if (_state.Length < entityCount)
        {
            var capacity = entityCount + entityCount / 4 + 16;
            _detailed = new byte[capacity * DetailedSize];
            _coarse = new byte[capacity * CoarseSize];
            _described = null;
            _state = new int[capacity];
            return;
        }

        Array.Clear(_state, 0, entityCount);
    }

    // The PLAYERINFO answer for the entity, built on the first request of the frame.
    public ReadOnlySpan<byte> Description(int index, in EntityView view, WorldData data)
    {
        var described = Volatile.Read(ref _described);
        if (described is null)
        {
            Interlocked.CompareExchange(ref _described, new byte[_state.Length * PlayerInfoPacket.Size], null);
            described = Volatile.Read(ref _described)!;
        }

        var entry = described.AsSpan(index * PlayerInfoPacket.Size, PlayerInfoPacket.Size);
        if (Claim(index, DescriptionBusy, DescriptionDone))
        {
            PresenceEncoder.DescriptionPacket(view, view.Revision, data).Write(entry);
            Interlocked.Or(ref _state[index], DescriptionDone);
        }

        return entry;
    }

    public ReadOnlySpan<byte> Detailed(int index, in EntityView view, int field)
    {
        var entry = _detailed.AsSpan(index * DetailedSize, DetailedSize);
        if (Claim(index, DetailedBusy, DetailedDone))
        {
            WireMapping.WritePlayData(entry, view, field, view.Revision);
            Interlocked.Or(ref _state[index], DetailedDone);
        }

        return entry;
    }

    public ReadOnlySpan<byte> Coarse(int index, in EntityView view)
    {
        var entry = _coarse.AsSpan(index * CoarseSize, CoarseSize);
        if (Claim(index, CoarseBusy, CoarseDone))
        {
            WireMapping.Coarse(view).Write(entry);
            Interlocked.Or(ref _state[index], CoarseDone);
        }

        return entry;
    }

    // True when the caller must encode the entry; otherwise returns once another encoder finished it.
    private bool Claim(int index, int busy, int done)
    {
        ref var state = ref _state[index];
        if ((Volatile.Read(ref state) & done) != 0)
        {
            return false;
        }

        if ((Interlocked.Or(ref state, busy) & busy) == 0)
        {
            return true;
        }

        var spin = new SpinWait();
        while ((Volatile.Read(ref state) & done) == 0)
        {
            spin.SpinOnce();
        }

        return false;
    }
}
