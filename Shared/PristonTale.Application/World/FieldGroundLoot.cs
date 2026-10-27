using System.Numerics;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Application.World;

public sealed class FieldGroundLoot(ItemCatalog catalog)
{
    public const float PickupReach = 32f;
    public const int Capacity = 1024;
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly List<Guid> _expired = [];
    private GroundDropState[] _snapshot = [];
    private bool _dirty;
    private long _nowMs;

    public int Count => _entries.Count;

    // Each dropped item is made when it falls: its values are rolled from the item's ranges with the field's stream.
    public void Place(KillPayout payout, Vector3 position, bool publicDrop, long nowMs, ISimRng rng)
    {
        foreach (var drop in payout.Drops)
        {
            if (_entries.Count >= Capacity)
            {
                break;
            }

            ItemDefinition? definition = null;
            if (drop.Kind == DropKind.Item && !catalog.TryGet(drop.ItemCode, out definition))
            {
                throw new InvalidDataException($"Unresolved rolled item {drop.ItemCode}.");
            }

            if (drop.ToInventory)
            {
                throw new NotSupportedException("Quest/event inventory delivery requires its own durable reward transaction.");
            }

            bool gold = drop.Kind == DropKind.Gold;
            var id = Guid.NewGuid();
            // A sealed puzzle piece lies on the ground as the lucky bag.
            var view = drop.Sealed
                ? new GroundDropView(id, ItemCodes.Parse(Puzzles.LuckyBagIcon), Puzzles.LuckyBagIcon, Puzzles.LuckyBagName, drop.Amount, false, position)
                : new GroundDropView(id, drop.ItemCode, gold ? "GG101" : definition!.CatalogKey, gold ? "Gold" : definition!.Name, drop.Amount, gold, position);
            var item = gold ? null : ItemRoller.Create(definition!, drop.Amount, rng);
            if (item is not null)
            {
                item.Sealed = drop.Sealed;
            }

            _entries.Add(id, new Entry(view, item, payout.CreditedPlayerId, publicDrop ? nowMs : nowMs + 5000, nowMs + LifetimeMs(definition)));
            _dirty = true;
        }
    }

    // An item copy or gold a game master made at a position; it is public at once and lasts as a monster drop does.
    public bool PlaceCreated(ItemInstance? item, int gold, Vector3 position, ulong ownerId, long nowMs)
    {
        if (_entries.Count >= Capacity || item is null && gold <= 0)
        {
            return false;
        }

        var id = Guid.NewGuid();
        var view = item is null
            ? new GroundDropView(id, 0u, "GG101", "Gold", gold, true, position)
            : new GroundDropView(id, item.Definition.Code, item.Definition.CatalogKey, item.Definition.Name, item.Count, false, position);
        _entries.Add(id, new Entry(view, item, ownerId, nowMs, nowMs + LifetimeMs(item?.Definition)));
        _dirty = true;
        return true;
    }

    public void Step(long nowMs)
    {
        _nowMs = nowMs;
        _expired.Clear();
        foreach (var (id, entry) in _entries)
        {
            if (entry.Reservation == Guid.Empty && nowMs >= entry.ExpiresAt)
            {
                _expired.Add(id);
            }
            else if (!entry.Public && nowMs >= entry.PublicAt)
            {
                entry.Public = true;
                _dirty = true;
            }
        }

        foreach (var id in _expired)
        {
            RemoveEntry(id);
        }
    }

    public GroundDropState[] Snapshot()
    {
        if (_dirty)
        {
            _snapshot = _entries.Values.Select(entry => new GroundDropState(entry.View, entry.OwnerId, entry.Public)).ToArray();
            _dirty = false;
        }

        return _snapshot;
    }

    public bool TryReserve(Guid id, Guid operation, WorldPlayer player, out GroundDropView? view, out ItemInstance? item)
    {
        view = null;
        item = null;
        if (operation == Guid.Empty || player.IsDead || !_entries.TryGetValue(id, out var entry)
            || _nowMs >= entry.ExpiresAt || entry.Reservation != Guid.Empty || (!entry.Public && entry.OwnerId != player.EntityId))
        {
            return false;
        }

        var delta = player.Position - entry.View.Position;
        if (delta.X * delta.X + delta.Z * delta.Z > PickupReach * PickupReach)
        {
            return false;
        }

        entry.Reservation = operation;
        view = entry.View;
        if (entry.Item is not null)
        {
            item = entry.Item.Copy(entry.Item.Count, entry.View.Id);
        }

        return true;
    }

    public void Resolve(Guid id, Guid operation, bool committed)
    {
        if (!_entries.TryGetValue(id, out var entry) || entry.Reservation != operation)
        {
            throw new InvalidOperationException("Ground-item reservation changed during storage.");
        }

        if (committed)
        {
            RemoveEntry(id);
        }
        else
        {
            entry.Reservation = Guid.Empty;
        }
    }

    // Gold (no definition), life and stamina potions lie for 90 s; mana potions and other items for 180 s.
    private static long LifetimeMs(ItemDefinition? definition) =>
        definition is null || definition.CatalogKey.StartsWith("PL", StringComparison.OrdinalIgnoreCase)
        || definition.CatalogKey.StartsWith("PS", StringComparison.OrdinalIgnoreCase) ? 90_000 : 180_000;

    private void RemoveEntry(Guid id)
    {
        if (_entries.Remove(id))
        {
            _dirty = true;
        }
    }

    private sealed class Entry(GroundDropView view, ItemInstance? item, ulong ownerId, long publicAt, long expiresAt)
    {
        public GroundDropView View { get; } = view;
        public ItemInstance? Item { get; } = item;
        public ulong OwnerId { get; } = ownerId;
        public long PublicAt { get; } = publicAt;
        public long ExpiresAt { get; } = expiresAt;
        public bool Public { get; set; }
        public Guid Reservation { get; set; }
    }
}
