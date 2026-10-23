using System.Threading.Channels;

namespace PristonTale.Application.World;

public sealed class WorldOutbox : Channel<WorldSnapshot>
{
    private readonly object _gate = new();
    private readonly Channel<bool> _ready = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropWrite,
        AllowSynchronousContinuations = false
    });
    private readonly Queue<CombatEventView[]> _events = new();
    private readonly List<SkillEventView> _skillEvents = new();
    private readonly List<StatusEventView> _statusEvents = new();
    private readonly List<ItemEffectView> _itemEffects = new();
    private SkillCooldownView[]? _cooldowns;
    private readonly QuestUpdateBuffer _quest = new();
    private readonly int _eventCapacity;
    private WorldSnapshot _latest;
    private int _eventCount;
    private bool _hasState;
    private bool _completed;

    public WorldOutbox(int eventCapacity = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventCapacity);
        _eventCapacity = eventCapacity;
        Reader = new OutboxReader(this);
        Writer = new OutboxWriter(this);
    }

    private bool Publish(WorldSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return false;
            }

            var incoming = snapshot.Events.Length + snapshot.SkillEvents.Length + snapshot.StatusEvents.Length + snapshot.ItemEffects.Length;
            if (incoming > _eventCapacity - _eventCount)
            {
                _completed = true;
                _ready.Writer.TryComplete(new InvalidOperationException("Reliable world-event queue overflow; reconnect required."));
                return false;
            }

            _latest = snapshot;
            _hasState = true;
            if (snapshot.Events.Length > 0)
            {
                _events.Enqueue(snapshot.Events);
                _eventCount += snapshot.Events.Length;
            }

            // Skill and status changes are as reliable as combat events; the newest cooldown list replaces older ones.
            _skillEvents.AddRange(snapshot.SkillEvents);
            _statusEvents.AddRange(snapshot.StatusEvents);
            _itemEffects.AddRange(snapshot.ItemEffects);
            _eventCount += snapshot.SkillEvents.Length + snapshot.StatusEvents.Length + snapshot.ItemEffects.Length;
            if (snapshot.Cooldowns is not null)
            {
                _cooldowns = snapshot.Cooldowns;
            }

            // Quest notices are kept in order up to their own bound; the newest journal view and opened dialog replace older ones.
            _quest.Add(snapshot.Quest);
            _ready.Writer.TryWrite(true);
            return true;
        }
    }

    private bool Read(out WorldSnapshot snapshot)
    {
        lock (_gate)
        {
            if (!_hasState)
            {
                snapshot = default;
                return false;
            }

            CombatEventView[] events;
            if (_events.Count == 0)
            {
                events = Array.Empty<CombatEventView>();
            }
            else if (_events.Count == 1)
            {
                // Snapshot batches are immutable after publication. Normal-rate subscribers can share
                // the field's one batch instead of allocating/copying it once per connection per tick.
                events = _events.Dequeue();
            }
            else
            {
                events = new CombatEventView[_eventCount];
                var offset = 0;
                while (_events.TryDequeue(out var batch))
                {
                    batch.CopyTo(events, offset);
                    offset += batch.Length;
                }
            }

            var latest = _latest;
            if (latest.Frame is { } frame)
            {
                // A frame the field already reused (only possible for a connection that left its field long ago) carries
                // no entity state any more.
                latest = frame.TryPin(latest.FrameGeneration)
                    ? latest.WithPin()
                    : new WorldSnapshot(latest.ServerTick, [], latest.Events, latest.FieldId, latest.Owner, latest.GroundDrops);
            }

            snapshot = latest.WithEvents(events) with
            {
                SkillEvents = _skillEvents.Count == 0 ? [] : _skillEvents.ToArray(),
                StatusEvents = _statusEvents.Count == 0 ? [] : _statusEvents.ToArray(),
                ItemEffects = _itemEffects.Count == 0 ? [] : _itemEffects.ToArray(),
                Cooldowns = _cooldowns,
                Quest = _quest.Take()
            };
            _skillEvents.Clear();
            _statusEvents.Clear();
            _itemEffects.Clear();
            _cooldowns = null;
            _latest = default;
            _eventCount = 0;
            _hasState = false;
            _ready.Reader.TryRead(out _);
            return true;
        }
    }

    private bool Complete(Exception? exception)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return false;
            }

            _completed = true;
            return _ready.Writer.TryComplete(exception);
        }
    }

    internal sealed class OutboxWriter(WorldOutbox owner) : ChannelWriter<WorldSnapshot>
    {
        public override bool TryWrite(WorldSnapshot item) => owner.Publish(item);
        public override bool TryComplete(Exception? error = null) => owner.Complete(error);

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(!Volatile.Read(ref owner._completed));
        }
    }

    private sealed class OutboxReader(WorldOutbox owner) : ChannelReader<WorldSnapshot>
    {
        public override Task Completion => owner._ready.Reader.Completion;
        public override bool TryRead(out WorldSnapshot item) => owner.Read(out item);
        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            return owner._ready.Reader.WaitToReadAsync(cancellationToken);
        }
    }
}
