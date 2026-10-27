using PristonTale.Engine3D.Formats;

namespace PristonTale.Gameplay.Combat;

public enum MotionCommandKind : byte
{
    None = 0,

    Set = 1,

    Restart = 2,
}

public readonly record struct MotionCommand(MotionCommandKind Kind, PristonActorMotion Motion)
{
    public static readonly MotionCommand None = new(MotionCommandKind.None, PristonActorMotion.Stand);
}

public sealed class ActorMotionController
{
    public PristonActorMotion Current { get; private set; } = PristonActorMotion.Stand;

    public bool IsDead => Current == PristonActorMotion.Dead;

    // A landing holds the player until its clip ends, as every motion from 0x100 up does in the original.
    public bool LocomotionFrozen => Current is PristonActorMotion.Damage or PristonActorMotion.Dead or PristonActorMotion.Eat
        or PristonActorMotion.FallStand or PristonActorMotion.FallDamage or PristonActorMotion.Yahoo;

    public MotionCommand ApplyAuthoritative(PristonActorMotion motion)
    {
        if (motion == Current)
        {
            return MotionCommand.None;
        }

        Current = motion;
        return new MotionCommand(IsOneShotBusy(motion) ? MotionCommandKind.Restart : MotionCommandKind.Set, motion);
    }

    // A fall keeps its motion while the player walks; the landing replaces it.
    public MotionCommand ApplyLocomotion(PristonActorMotion motion)
    {
        if (LocomotionFrozen || motion == Current || Current == PristonActorMotion.FallDown)
        {
            return MotionCommand.None;
        }

        Current = motion;
        return new MotionCommand(MotionCommandKind.Set, motion);
    }

    public MotionCommand Retrigger(PristonActorMotion oneShot)
    {
        if (IsDead)
        {
            return MotionCommand.None;
        }

        Current = oneShot;
        return new MotionCommand(MotionCommandKind.Restart, oneShot);
    }

    private static bool IsOneShotBusy(PristonActorMotion motion) => motion is
        PristonActorMotion.Attack or
        PristonActorMotion.Damage or
        PristonActorMotion.Skill or
        PristonActorMotion.Eat or
        PristonActorMotion.Yahoo or
        PristonActorMotion.Restart;
}
