using System.Numerics;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Physics;

namespace PristonTale.Gameplay.World.Remote;

internal sealed class RemoteCollisionRoster
{
    private readonly record struct Entry(RemoteEntityKind Kind, Vector3 Position, int Hp);
    private readonly Dictionary<uint, Entry> _entries = [];

    public void Spawn(EntitySpawnInfo spawn)
    {
        if (spawn.Kind is RemoteEntityKind.Player or RemoteEntityKind.Monster or RemoteEntityKind.Npc)
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

    public void Remove(uint id) => _entries.Remove(id);
    public void Clear() => _entries.Clear();

    public void Collect(uint excludeId, List<EntityNeighbour> into)
    {
        into.Clear();
        foreach (var (id, entry) in _entries)
        {
            if (id == excludeId || entry.Hp <= 0)
            {
                continue;
            }

            var radius = entry.Kind == RemoteEntityKind.Player ? EntityCollisionRadii.Player : EntityCollisionRadii.Monster;
            into.Add(new EntityNeighbour(entry.Position, radius));
        }
    }
}
