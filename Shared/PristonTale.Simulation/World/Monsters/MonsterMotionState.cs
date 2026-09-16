namespace PristonTale.Simulation.World.Monsters;

public enum MonsterMotionState
{
    Stand = 0,

    Walk = 1,

    Run = 2,

    Attack = 3,

    Damage = 4,

    Dead = 5,

    Skill = 6
}

public static class MonsterMotionStateExtensions
{
    public static bool IsBusy(this MonsterMotionState state)
        => state is MonsterMotionState.Attack or MonsterMotionState.Skill or MonsterMotionState.Damage or MonsterMotionState.Dead;

    public static bool IsMoving(this MonsterMotionState state)
        => state is MonsterMotionState.Walk or MonsterMotionState.Run;
}
