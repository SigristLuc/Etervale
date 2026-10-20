namespace PristonTale.Simulation.Characters;

public static class AnimationRate
{
    // Baseline animation frame step of the original client. All factors are frame step / Baseline.
    private const float Baseline = 80f;
    // The move speed used in the walk/run frame step is capped at 350.
    private const int MoveSpeedCap = 350;
    // Walk/run frame step = (80 * move speed) / 300.
    private const int MoveDivisor = 300;
    // Attack speed = 256 + 32 * clamp(stat - 6, 0, 6) in 8-bit fixed point (256 = 1.0).
    private const int FixedOne = 256;

    public static float MoveRate(int perTickTranslation)
    {
        int moveSpeed = Math.Min(perTickTranslation, MoveSpeedCap);
        // Match the original integer math: FrameStep = (80*moveSpeed)/300 (int), then / 80.0 to normalize.
        int frameStep = (80 * moveSpeed) / MoveDivisor;
        return frameStep / Baseline;
    }

    public static int AttackSpeedMainFrame(int attackSpeedStat)
    {
        int cnt = Math.Clamp(attackSpeedStat - 6, 0, 6);
        return FixedOne + 32 * cnt;
    }

    public static float AttackRate(int attackSpeedStat)
    {
        int attackSpeed = AttackSpeedMainFrame(attackSpeedStat);
        // FrameStep = (80*attackSpeed)>>8 (int), then / 80.0 to normalize against the baseline.
        int frameStep = (80 * attackSpeed) >> 8;
        return frameStep / Baseline;
    }
}
