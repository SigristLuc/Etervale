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
}

public sealed record CombatEventInfo(
    uint AttackerId,
    uint TargetId,
    RemoteCombatEventKind Kind,
    float DurationMs,
    float HitAtMs,
    int Damage,
    bool Critical,
    bool Killed,
    int Resources = 0,
    int SourceSoundCode = 0,
    Vector3 Position = default,
    float Facing = 0f);
