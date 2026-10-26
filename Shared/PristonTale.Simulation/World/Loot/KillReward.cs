namespace PristonTale.Simulation.World.Loot;

public static class KillReward
{
    // The experience event's extra experience: any setting up to 199 adds 100%, then steps of 100% up to 400%.
    public static int EventBonusPercent(int setting) => setting <= 0 ? 0 : Math.Clamp(setting / 100, 1, 4) * 100;

    public static ulong ExpForKill(ulong baseExp, int monsterLevel, int playerLevel, int multiplier = 1, int eventPercent = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(multiplier);
        long difference = Math.Abs((long)monsterLevel - playerLevel);
        int percent = difference switch
        {
            <= 10 => 100,
            <= 20 => 85,
            <= 30 => 70,
            _ => 55
        };
        // Event bonus precedes the experience total; computed in 128 bits and saturated, so no award wraps or throws.
        var total = (UInt128)baseExp * (uint)multiplier * (uint)(100 + EventBonusPercent(eventPercent)) / 100 * (uint)percent / 100;
        return total > ulong.MaxValue ? ulong.MaxValue : (ulong)total;
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
