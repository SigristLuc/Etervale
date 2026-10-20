namespace PristonTale.Simulation.World.Loot;

public static class KillReward
{
    public static int ExpForKill(int baseExp, int monsterLevel, int playerLevel, int multiplier = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseExp);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(multiplier);
        long difference = Math.Abs((long)monsterLevel - playerLevel);
        int percent = difference switch
        {
            <= 10 => 100,
            <= 20 => 85,
            <= 30 => 70,
            _ => 55
        };
        // Event bonus precedes the experience total; widen before multiplying to avoid overflow.
        long boosted = checked((long)baseExp * multiplier);
        return checked((int)(boosted * percent / 100));
    }

    public static bool FirstAttackerStealsCredit(
        bool firstAttackerEqualsKiller,
        int firstAttackerAccumulatedDamage,
        int monsterMaxLife,
        bool firstAttackerStillEngaged)
    {
        if (firstAttackerEqualsKiller || !firstAttackerStillEngaged)
            return false;
        return firstAttackerAccumulatedDamage >= (monsterMaxLife * 40) / 100;
    }

    public const long FirstAttackerDisengageMs = 15000;
}
