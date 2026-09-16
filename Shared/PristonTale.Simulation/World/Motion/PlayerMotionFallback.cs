using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.World.Motion;

public static class PlayerMotionFallback
{
    public const int SwingFrames = 40;

    public const int HitFrames = 22;

    public const int FlinchFrames = 22;

    public static MotionClip GetClip(PlayerClass playerClass, int state)
    {
        var (attackEvent, damageFrames, eatEvent) = playerClass switch
        {
            PlayerClass.Mechanician or PlayerClass.Fighter or PlayerClass.Knight => (3520u, 22, 2880u),
            PlayerClass.Archer or PlayerClass.Atalanta or PlayerClass.Assassin => (3200u, 24, 2400u),
            PlayerClass.Pikeman => (3680u, 22, 2560u),
            PlayerClass.Priestess => (3200u, 20, 3040u),
            PlayerClass.Magician or PlayerClass.Shaman => (3360u, 22, 3040u),
            _ => throw new ArgumentOutOfRangeException(nameof(playerClass))
        };

        // Clip timing uses spans and event offsets; frame windows are normalized to a zero origin.
        return state switch
        {
            MotionStateCode.Attack => new MotionClip(0, 40, attackEvent, 0, 0, 0, false),
            MotionStateCode.Damage => new MotionClip(0, damageFrames, 0, 0, 0, 0, false),
            MotionStateCode.Eat => new MotionClip(0, 20, eatEvent, 0, 0, 0, false),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
    }
}
