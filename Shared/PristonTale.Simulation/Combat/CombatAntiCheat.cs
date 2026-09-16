namespace PristonTale.Simulation.Combat;

public readonly record struct AntiCheatVerdict(bool Reject, bool Blacklist, int Code, string Reason)
{
    public static readonly AntiCheatVerdict Ok = new(false, false, 0, "ok");
}

public static class CombatAntiCheat
{
    public const int RateWindowMs = 5000;

    public const int RateRejectCount = 40;

    public const int RateBlacklistCount = 80;

    public const int PowerHardCap = 9000;

    public static AntiCheatVerdict CheckRate(int attacksInWindow)
    {
        if (attacksInWindow >= RateBlacklistCount)
            return new AntiCheatVerdict(Reject: true, Blacklist: true, Code: 1823, Reason: "attack rate >= 80/5s");
        if (attacksInWindow >= RateRejectCount)
            return new AntiCheatVerdict(Reject: true, Blacklist: false, Code: 1823, Reason: "attack rate >= 40/5s");
        return AntiCheatVerdict.Ok;
    }

    public static AntiCheatVerdict CheckPower(int power)
    {
        if (power >= PowerHardCap)
            return new AntiCheatVerdict(Reject: true, Blacklist: true, Code: 1821, Reason: "power >= 9000");
        return AntiCheatVerdict.Ok;
    }

    public static AntiCheatVerdict CheckMonotonic(int count, int lastCount, long timeMs, long lastTimeMs)
    {
        if (count <= lastCount || timeMs <= lastTimeMs)
            return new AntiCheatVerdict(Reject: true, Blacklist: false, Code: 0, Reason: "non-monotonic attack counter/time");
        return AntiCheatVerdict.Ok;
    }
}
