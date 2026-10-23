using System.Numerics;

namespace PristonTale.Simulation.World.Ai;

// SightPercent scales the sight at which monsters notice the prey (stealth lowers it); 100 is unchanged.
public readonly record struct PreyCandidate(uint EntityId, Vector3 Position, bool IsAlive, int SightPercent = 100);

public interface IPreyView
{
    IReadOnlyList<PreyCandidate> Candidates { get; }

    IReadOnlyList<PreyCandidate> Nearby(Vector3 position, float radius) => Candidates;

    bool TryGet(uint entityId, out PreyCandidate prey);
}
