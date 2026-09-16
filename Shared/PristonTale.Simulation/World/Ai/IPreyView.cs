using System.Numerics;

namespace PristonTale.Simulation.World.Ai;

public readonly record struct PreyCandidate(uint EntityId, Vector3 Position, bool IsAlive);

public interface IPreyView
{
    IReadOnlyList<PreyCandidate> Candidates { get; }

    IReadOnlyList<PreyCandidate> Nearby(Vector3 position, float radius) => Candidates;

    bool TryGet(uint entityId, out PreyCandidate prey);
}
