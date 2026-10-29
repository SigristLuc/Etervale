using PristonTale.Application.World;
using PristonTale.Protocol.Packets;

namespace PristonTale.Server.Net;

// The descriptions of one frame, encoded once for every receiver of the frame. An entity's description depends only on
// its view and its field-wide descriptor revision, so every encoder copies the same bytes; the first encoder that needs
// one writes it while the others wait the few nanoseconds it takes.
internal sealed class SharedPresenceWire : IPresenceFrameAttachment
{
    private const int Slot = EntityDescriptionPacket.MaximumSize;
    private const int Busy = 1;
    private const int Done = 2;
    private byte[]? _described;
    private int[] _describedLength = [];
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
            _describedLength = new int[capacity];
            _described = null;
            _state = new int[capacity];
            return;
        }

        Array.Clear(_state, 0, entityCount);
    }

    // The description of the entity, built on the first request of the frame; the buffer is allocated by the frame's
    // first description and kept while the frame is reused.
    public ReadOnlySpan<byte> Description(int index, in EntityView view, WorldData data)
    {
        var described = Volatile.Read(ref _described);
        if (described is null)
        {
            Interlocked.CompareExchange(ref _described, new byte[_state.Length * Slot], null);
            described = Volatile.Read(ref _described)!;
        }

        var entry = described.AsSpan(index * Slot, Slot);
        if (Claim(index))
        {
            _describedLength[index] = PresenceEncoder.DescriptionPacket(view, view.Revision, data).Write(entry);
            Interlocked.Or(ref _state[index], Done);
        }

        return entry[.._describedLength[index]];
    }

    // True when the caller must encode the description; otherwise returns once another encoder finished it.
    private bool Claim(int index)
    {
        ref var state = ref _state[index];
        if ((Volatile.Read(ref state) & Done) != 0)
        {
            return false;
        }

        if ((Interlocked.Or(ref state, Busy) & Busy) == 0)
        {
            return true;
        }

        var spin = new SpinWait();
        while ((Volatile.Read(ref state) & Done) == 0)
        {
            spin.SpinOnce();
        }

        return false;
    }
}
