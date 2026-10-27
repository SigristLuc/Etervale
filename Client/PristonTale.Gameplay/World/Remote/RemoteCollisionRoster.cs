using System.Numerics;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Physics;
using PristonTale.Contracts.World;

namespace PristonTale.Gameplay.World.Remote;

internal sealed class RemoteCollisionRoster
{
    private readonly record struct Entry(EntityKind Kind, Vector3 Position, int Hp);
    private readonly Dictionary<ulong, Entry> _entries = [];

    public void Spawn(EntitySpawnInfo spawn)
    {
        if (spawn.Kind is EntityKind.Player or EntityKind.Monster or EntityKind.Npc)
        {
            _entries[spawn.EntityId] = new Entry(spawn.Kind, spawn.Position, spawn.Hp);
        }
    }

    public void Update(EntityStateInfo state)
    {
        if (_entries.TryGetValue(state.EntityId, out var entry))
        {
            _entries[state.EntityId] = entry with { Position = state.Position, Hp = state.Hp };
        }
    }

    public void Remove(ulong id) => _entries.Remove(id);
    public void Clear() => _entries.Clear();

    public void Collect(ulong excludeId, List<EntityNeighbour> into)
    {
        into.Clear();
        foreach (var (id, entry) in _entries)
        {
            if (id == excludeId || entry.Hp <= 0)
            {
                continue;
            }

            var radius = entry.Kind == EntityKind.Player ? EntityCollisionRadii.Player : EntityCollisionRadii.Monster;
            into.Add(new EntityNeighbour(entry.Position, radius));
        }
    }
}
