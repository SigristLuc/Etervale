using PristonTale.Contracts.Motion;

namespace PristonTale.Simulation.World.Monsters;

// The monster AI reads its motion through these.
public static class MotionStateExtensions
{
    public static bool IsBusy(this MotionState state)
        => state is MotionState.Attack or MotionState.Skill or MotionState.Damage or MotionState.Dead
            or MotionState.Eat or MotionState.Warp;

    public static bool IsMoving(this MotionState state)
        => state is MotionState.Walk or MotionState.Run;
}
