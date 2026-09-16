namespace PristonTale.Simulation.Characters;

public static class Locomotion
{
    private const int TicksPerSecond = 70;   // original fixed tick rate
    private const float FixedOne = 256f;     // fONE
    private const int WalkFieldFactor = 180; // >>8  ≈ 0.703x outside villages
    private const int RunFactor = 460;       // >>8  ≈ 1.797x

    public static int BaseTranslation(int moveSpeedStat)
    {
        int level = Math.Clamp(moveSpeedStat, 1, 9);
        return 250 + (10 * (level - 1));
    }

    public static float WalkUnitsPerSecond(int moveSpeedStat, bool inVillage)
    {
        int baseTranslation = BaseTranslation(moveSpeedStat);
        int perTick = inVillage ? baseTranslation : (baseTranslation * WalkFieldFactor) >> 8;
        return PerTickToUnitsPerSecond(perTick);
    }

    public static float RunUnitsPerSecond(int moveSpeedStat)
        => PerTickToUnitsPerSecond((BaseTranslation(moveSpeedStat) * RunFactor) >> 8);

    private static float PerTickToUnitsPerSecond(int perTick)
        => perTick / FixedOne * TicksPerSecond;
}
