using System.Numerics;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Application.World;

public sealed class FieldGroundLoot(ItemCatalog catalog)
{
    public const float PickupReach = 32f;
    public const int Capacity = 1024;
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly Dictionary<uint, Guid> _handles = new();
    private uint _nextHandle;
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
            var handle = NextHandle();
            var view = new GroundDropView(id, drop.ItemCode, gold ? "GG101" : definition!.CatalogKey,
                gold ? "Gold" : definition!.Name, drop.Amount, gold, position) { Handle = handle };
            _handles.Add(handle, id);
            // As on the original server, gold, life and stamina potions expire in 90s; mana and other items in 180s.
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
            RemoveEntry(id);
        }
        else
        {
            entry.Reservation = Guid.Empty;
        }
    }

    // Clients address drops by the 32-bit handle carried in the ground item record's lpStgArea; stored item ids stay random GUIDs.
    public Guid ResolveClientId(Guid clientId) =>
        GroundDropHandle.TryParse(clientId, out var handle) && _handles.TryGetValue(handle, out var id) ? id : clientId;

    private uint NextHandle()
    {
        do
        {
            _nextHandle++;
        }
        while (_nextHandle == 0 || _handles.ContainsKey(_nextHandle));

        return _nextHandle;
    }

    private void RemoveEntry(Guid id)
    {
        if (_entries.Remove(id, out var entry))
        {
            _handles.Remove(entry.View.Handle);
            _dirty = true;
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
