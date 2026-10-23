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
    private byte[] _detailed = [];
    private byte[] _coarse = [];
    private int[] _state = [];

    private SharedPresenceWire(int entityCount) => Reset(entityCount);

    public static SharedPresenceWire Create(int entityCount) => new(entityCount);

    // Field tick only, while no encoder holds the frame.
    public void Reset(int entityCount)
    {
        if (_state.Length != entityCount)
        {
            _detailed = new byte[entityCount * DetailedSize];
            _coarse = new byte[entityCount * CoarseSize];
            _state = new int[entityCount];
            return;
        }

        Array.Clear(_state);
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
