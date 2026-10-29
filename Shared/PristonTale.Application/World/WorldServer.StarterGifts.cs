using System.Threading.Channels;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Application.World;

// What a starter gift gave: the item's name, or null when it found no room (it is offered again after the interval).
public sealed record StarterGiftOutcome(string? ItemName);

// The starter gifts of new characters (StarterGifts): the tick hands a playing character whose gift is due to the gift
// service, which gives it as a durable operation; the next comes StarterGifts.Interval after that one ended.
public sealed partial class WorldServer
{
    private readonly Channel<PlayerSession> _starterGifts = SessionRequests();

    private uint _serverTick;

    public ChannelReader<PlayerSession> StarterGiftRequests => _starterGifts.Reader;

    // Work the tick queues for a session and a service runs off the tick (quest upkeep, starter gifts).
    private static Channel<PlayerSession> SessionRequests() => Channel.CreateUnbounded<PlayerSession>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false
    });

    private uint GiftTick(TimeSpan after) => _serverTick + (uint)(after.TotalSeconds * TickHz);

    private void OfferStarterGifts(uint serverTick)
    {
        if (!_options.GiveStarterGifts || serverTick % TickHz != 0)
        {
            return;
        }

        foreach (var connection in _connections.Values)
        {
            if (connection.Session is { IsPlaying: true } session && !connection.GiftRequested && serverTick >= connection.NextGiftTick
                && connection.Player.Character.StarterGiftsReceived < StarterGifts.Count)
            {
                connection.GiftRequested = true;
                _starterGifts.Writer.TryWrite(session);
            }
        }
    }

    // The next gift: a perfect copy worn on the first free slot that takes it, otherwise left in the bag. A gift the
    // catalog lacks counts as given; one without room waits (no snapshot).
    public Task<PreparedCharacterOperation<StarterGiftOutcome>?> PrepareStarterGiftAsync(PlayerSession session)
    {
        return EnqueueAsync<PreparedCharacterOperation<StarterGiftOutcome>?>(NoOwner, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection)
                || connection.Player.Character.StarterGiftsReceived >= StarterGifts.Count)
            {
                return null;
            }

            var original = connection.Player.Character;
            var item = StarterGifts.Create(_worldData.Current.Items, original.Definition.Class, original.StarterGiftsReceived, _fields[connection.FieldId].Rng);
            var (candidate, snapshot) = Receive(session, connection, item, candidate =>
            {
                if (item is not null)
                {
                    candidate.WearIfFree(item.InstanceId);
                }

                candidate.CountStarterGift();
            });
            return new PreparedCharacterOperation<StarterGiftOutcome>(original, candidate, snapshot,
                new StarterGiftOutcome(candidate is null ? null : item?.DisplayName));
        }, waitForCapacity: true);
    }

    public Task CommitStarterGiftAsync(PlayerSession session, PreparedCharacterOperation<StarterGiftOutcome> operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            var candidate = CheckReceived(session, operation, "a starter gift");
            operation.Original.CommitInventoryOperation(candidate);
            operation.Original.RestoreStarterGifts(candidate.StarterGiftsReceived);
            return true;
        }, waitForCapacity: true);
    }

    // The gift service is done with the session's request: the next gift is due after the interval.
    public Task EndStarterGiftAsync(PlayerSession session)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (TryGetOwnConnection(session, out var connection))
            {
                connection.GiftRequested = false;
                connection.NextGiftTick = GiftTick(StarterGifts.Interval);
            }

            return true;
        }, waitForCapacity: true);
    }

    // Tick only. A copy of the playing character with the item (if any) in its bag, changed by adjust, and its capture;
    // no copy when the item does not fit.
    private (PlayerCharacter? Candidate, CharacterSaveSnapshot? Snapshot) Receive(PlayerSession session, Connection connection, ItemInstance? item,
        Action<PlayerCharacter>? adjust)
    {
        var candidate = connection.Player.Character.CloneForInventoryOperation();
        if (item is not null && !candidate.ReceiveGroundLoot(item, 0))
        {
            return (null, null);
        }

        adjust?.Invoke(candidate);
        return (candidate, CharacterRecordFactory.Capture(session.Loaded!, candidate, connection.FieldId, connection.Player.Position));
    }

    // Tick only. The candidate of an item received, once the session still owns the character it was copied from.
    private static PlayerCharacter CheckReceived(PlayerSession session, PreparedOperation operation, string what) =>
        ReferenceEquals(session.Character, operation.Original) && operation.Candidate is { } candidate
            ? candidate
            : throw new CommitInvariantException($"Character ownership changed during {what}.");
}
