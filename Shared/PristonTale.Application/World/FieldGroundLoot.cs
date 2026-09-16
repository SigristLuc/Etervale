using System.Numerics;
using PristonTale.Simulation.Items;
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

    public void Place(KillPayout payout, Vector3 position, bool publicDrop, long nowMs)
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
            var view = new GroundDropView(id, drop.ItemCode, gold ? "GG101" : definition!.CatalogKey,
                gold ? "Gold" : definition!.Name, drop.Amount, gold, position);
            // OnSever.cpp AddItem: gold, life and stamina potions expire in 90s; mana and other items in 180s.
            bool shortLived = gold || definition?.CatalogKey.StartsWith("PL", StringComparison.OrdinalIgnoreCase) == true
                || definition?.CatalogKey.StartsWith("PS", StringComparison.OrdinalIgnoreCase) == true;
            long lifetime = shortLived ? 90_000 : 180_000;
            _entries.Add(id, new Entry(view, definition, payout.CreditedPlayerId, publicDrop ? nowMs : nowMs + 5000, nowMs + lifetime));
            _dirty = true;
        }
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
            _entries.Remove(id);
            _dirty = true;
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
        if (entry.Definition is not null)
        {
            item = new ItemInstance(entry.Definition, entry.View.Amount) { InstanceId = entry.View.Id };
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
            _entries.Remove(id);
            _dirty = true;
        }
        else
        {
            entry.Reservation = Guid.Empty;
        }
    }

    private sealed class Entry(GroundDropView view, ItemDefinition? definition, uint ownerId, long publicAt, long expiresAt)
    {
        public GroundDropView View { get; } = view;
        public ItemDefinition? Definition { get; } = definition;
        public uint OwnerId { get; } = ownerId;
        public long PublicAt { get; } = publicAt;
        public long ExpiresAt { get; } = expiresAt;
        public bool Public { get; set; }
        public Guid Reservation { get; set; }
    }
}
