using System.Numerics;

namespace PristonTale.Simulation.Skills;

// Why the server refused a cast. Numbering matches the wire status of the cast result.
public enum SkillCastVerdict : byte
{
    Accepted = 0,
    NotLearned = 1,
    NotCastable = 2,
    Cooldown = 3,
    NotEnoughMana = 4,
    NotEnoughStamina = 5,
    InvalidTarget = 6,
    OutOfRange = 7,
    Busy = 8,
    Dead = 9,
    WeaponNotAllowed = 10,
    Stale = 11,
    InVillage = 12,
    Rejected = 13,
    // A buff that cannot be cast again while it runs.
    AlreadyActive = 14
}

// What a cast request asks: a new cast, the release of a held charge, or an order to the caster's summon.
public enum SkillCastCommand : byte
{
    Cast = 0,
    Release = 1,
    SummonPassive = 2,
    SummonAggressive = 3
}

// One cast request as the client aimed it: a target entity, a ground point or both. The server re-derives every
// value it trusts (rank, cost, range, facing).
public readonly record struct SkillCastRequest(uint Sequence, uint SkillCode, ulong TargetId, Vector3 Point, bool HasPoint,
    SkillCastCommand Command = SkillCastCommand.Cast);

public readonly record struct SkillCastOutcome(uint Sequence, uint SkillCode, SkillCastVerdict Verdict, int Rank, int CooldownMs, int RemainingMs,
    int Mana, int Stamina)
{
    public bool Accepted => Verdict == SkillCastVerdict.Accepted;

    public static SkillCastOutcome Refused(in SkillCastRequest request, SkillCastVerdict verdict, int remainingMs = 0) =>
        new(request.Sequence, request.SkillCode, verdict, 0, 0, remainingMs, 0, 0);
}
