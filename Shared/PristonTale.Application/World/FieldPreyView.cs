using System.Collections.Generic;
using System.Numerics;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Application.World;

public sealed class FieldPreyView : IPreyView
{
    private readonly List<PreyCandidate> _candidates = new();
    private readonly Dictionary<uint, PreyCandidate> _byId = new();
    private readonly SpatialIndex _index = new();
    private readonly List<SpatialEntry> _query = new();
    private readonly List<PreyCandidate> _nearby = new();

    public IReadOnlyList<PreyCandidate> Nearby(Vector3 position, float radius)
    {
        _index.Query(position, radius, _query);
        _nearby.Clear();
        foreach (var entry in _query)
        {
            _nearby.Add(_byId[entry.Id]);
        }

        return _nearby;
    }

    public IReadOnlyList<PreyCandidate> Candidates => _candidates;

    public bool TryGet(uint entityId, out PreyCandidate prey) => _byId.TryGetValue(entityId, out prey);

    public void Rebuild(IEnumerable<WorldPlayer> players)
    {
        _candidates.Clear();
        _byId.Clear();
        _index.Clear();
        foreach (var p in players)
        {
            var candidate = new PreyCandidate(p.EntityId, p.Position, p.Hp > 0);
            _candidates.Add(candidate);
            _index.Put(p.EntityId, p.Position, _candidates.Count - 1);
            _byId[p.EntityId] = candidate;
        }
    }
}
