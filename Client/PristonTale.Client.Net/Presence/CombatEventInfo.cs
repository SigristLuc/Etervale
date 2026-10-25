using System.Numerics;

namespace PristonTale.Client.Net.Presence;

public enum RemoteCombatEventKind
{
    Unspecified = 0,

    SwingBegin = 1,

    Hit = 2,
    PotionBegin = 3,
    PotionApplied = 4,
    MonsterSkillImpact = 5,
    // A skill or periodic effect restored life; Damage carries the amount.
    Heal = 6,
    // The target evaded a monster's attack through a skill effect.
    Evade = 7,
    // A dead player rose again (a revive skill); Damage carries the life it returns with.
    Revive = 8,
    // A player blocked a monster's normal attack; no damage reaches them.
    Block = 9,
    // A player's swing missed its target.
    Miss = 10,
}

public sealed record CombatEventInfo(
    ulong AttackerId,
    ulong TargetId,
    RemoteCombatEventKind Kind,
    float DurationMs,
    float HitAtMs,
    int Damage,
    bool Critical,
    bool Killed,
    int Resources = 0,
    int SourceSoundCode = 0,
    Vector3 Position = default,
    float Facing = 0f)
{
    // A monster's swing: the motion-table slot of its clip (-1 for the first clip of the state); a swing or hit of a
    // monster skill.
    public int Clip { get; init; } = -1;

    public bool Skill { get; init; }
}
