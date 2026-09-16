namespace PristonTale.Simulation.Characters;

public static class AnimationRate
{
    // PTClassic baseline FrameStep (character.cpp:1193, :4294). All factors are FrameStep / Baseline.
    private const float Baseline = 80f;
    // character.cpp:4283 — the MoveSpeed used in the walk/run FrameStep is capped at 350.
    private const int MoveSpeedCap = 350;
    // character.cpp:4287 — divisor in (80*MoveSpeed)/300.
    private const int MoveDivisor = 300;
    // playsub.cpp:6701-6710 — AttackSpeed = fONE + 32*clamp(stat-6,0,6); fONE = 256, FLOATNS = 8.
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
