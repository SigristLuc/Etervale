using Microsoft.Xna.Framework;
using PristonTale.Simulation.Combat;

namespace PristonTale.Gameplay.World.Remote;

public static class AttackPresentation
{
    public const float MeleeRange = MeleeReach.PlayerBaseRange;

    public enum Action
    {
        Approach,
        Attack,
    }

    public readonly record struct Decision(Action Action, Vector2 ApproachDirection, Vector2 Facing)
    {
        public bool ShouldAttack => Action == Action.Attack;
    }

    public static Decision Decide(Vector2 playerPlanar, Vector2 targetPlanar, float attackRange = MeleeRange)
    {
        var toTarget = targetPlanar - playerPlanar;
        var distSq = toTarget.LengthSquared();

        if (distSq <= 1e-6f)
        {
            return new Decision(Action.Attack, Vector2.Zero, Vector2.Zero);
        }

        var dir = toTarget / MathF.Sqrt(distSq);
        return distSq <= attackRange * attackRange
            ? new Decision(Action.Attack, Vector2.Zero, dir)
            : new Decision(Action.Approach, dir, dir);
    }

    // The forward a swing starts with: toward its target, as the server turns the attacker at every swing, whatever the
    // range; a target on the player's own spot keeps the current forward.
    public static Vector2 SwingFacing(Vector2 playerPlanar, Vector2 targetPlanar, Vector2 current)
    {
        var facing = Decide(playerPlanar, targetPlanar).Facing;
        return facing.LengthSquared() > 1e-6f ? facing : current;
    }
}
