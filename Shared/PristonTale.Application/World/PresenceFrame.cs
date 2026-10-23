namespace PristonTale.Application.World;

// What the network edge caches on a frame (encoded entries every receiver shares); the field resets it when it reuses
// the frame for a later tick.
public interface IPresenceFrameAttachment
{
    void Reset(int entityCount);
}

// The entity state of one field at one tick, shared by every receiver's snapshot of that tick: the views, their grid and
// whatever the edge encodes once for all receivers. Frames are reused by later ticks of the same field instead of being
// allocated per tick. A receiver pins the frame while it encodes; the field reuses only frames nobody pinned and that no
// outbox can still hand out (older than the field's latest published frame).
public sealed class PresenceFrame
{
    // -1 while the field rebuilds the frame; otherwise the number of encoders reading it.
    private int _readers;
    private int _generation;
    private IPresenceFrameAttachment? _attachment;

    internal PresenceFrame(int entityCount)
    {
        Views = new EntityView[entityCount];
        Grid = EntityGrid.Build(Views);
    }

    public EntityView[] Views { get; private set; }
    public EntityGrid Grid { get; }
    public int Generation => Volatile.Read(ref _generation);

    // Set when a snapshot of this frame went to a writer that never releases it (a plain channel in tests); such a
    // frame is never reused.
    internal bool Retained { get; set; }

    // The edge's shared cache for this frame; the first encoder creates it, the others reuse it.
    public T Attachment<T>(Func<int, T> create) where T : class, IPresenceFrameAttachment
    {
        if (Volatile.Read(ref _attachment) is T existing)
        {
            return existing;
        }

        var created = create(Views.Length);
        return Interlocked.CompareExchange(ref _attachment, created, null) as T ?? created;
    }

    // Pins the frame for reading if it still holds the given generation; false when the field already reused it.
    public bool TryPin(int generation)
    {
        var spin = new SpinWait();
        while (true)
        {
            var readers = Volatile.Read(ref _readers);
            if (readers < 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _readers, readers + 1, readers) == readers)
            {
                if (Volatile.Read(ref _generation) == generation)
                {
                    return true;
                }

                Interlocked.Decrement(ref _readers);
                return false;
            }

            spin.SpinOnce();
        }
    }

    public void Release() => Interlocked.Decrement(ref _readers);

    // Field tick only: claims an unpinned frame for a new tick, resizing the views when the entity count changed.
    internal bool TryRebuild(int entityCount)
    {
        if (Retained || Interlocked.CompareExchange(ref _readers, -1, 0) != 0)
        {
            return false;
        }

        _generation++;
        if (Views.Length != entityCount)
        {
            Views = new EntityView[entityCount];
        }

        _attachment?.Reset(entityCount);
        return true;
    }

    internal void Publish() => Volatile.Write(ref _readers, 0);
}

// The frames of one field: a small pool reused tick after tick by the field's own step.
internal sealed class FieldPresenceFrames
{
    // Beyond this many frames (encoders holding many ticks at once) new frames are not kept for reuse.
    private const int MaxFrames = 16;
    private readonly List<PresenceFrame> _frames = new();
    private PresenceFrame? _latest;

    public PresenceFrame Rent(int entityCount)
    {
        foreach (var frame in _frames)
        {
            if (!ReferenceEquals(frame, _latest) && frame.TryRebuild(entityCount))
            {
                return frame;
            }
        }

        _frames.RemoveAll(frame => frame.Retained);
        var created = new PresenceFrame(entityCount);
        created.TryRebuild(entityCount);
        if (_frames.Count < MaxFrames)
        {
            _frames.Add(created);
        }

        return created;
    }

    public void Published(PresenceFrame frame)
    {
        frame.Publish();
        _latest = frame;
    }
}
