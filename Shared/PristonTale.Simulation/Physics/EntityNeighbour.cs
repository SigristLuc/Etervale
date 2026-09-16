using System.Numerics;

namespace PristonTale.Simulation.Physics;

public readonly struct EntityNeighbour
{
    public readonly Vector3 Position;
    public readonly float Radius;

    public EntityNeighbour(Vector3 position, float radius)
    {
        Position = position;
        Radius = radius;
    }

    public EntityNeighbour(Vector3 position, EntityCollisionKind kind)
    {
        Position = position;
        Radius = EntityCollisionRadii.ForKind(kind);
    }
}
