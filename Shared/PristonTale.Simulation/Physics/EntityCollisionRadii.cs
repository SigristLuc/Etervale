namespace PristonTale.Simulation.Physics;

public static class EntityCollisionRadii
{
    public const float Player = 16f;
    public const float Monster = 32f;
    public const float Npc = 32f;

    public static float PairDistance(float moverRadius, float neighbourRadius) => MathF.Min(moverRadius, neighbourRadius);

    public static float ForKind(EntityCollisionKind kind)
    {
        return kind switch
        {
            EntityCollisionKind.Player => Player,
            EntityCollisionKind.Monster => Monster,
            EntityCollisionKind.Npc => Npc,
            _ => Monster
        };
    }
}
