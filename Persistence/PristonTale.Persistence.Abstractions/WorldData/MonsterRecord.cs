namespace PristonTale.Persistence.Abstractions.WorldData;

public sealed record MonsterRecord(
    string Name,
    string Model,
    int Level,
    int AttackMin,
    int AttackMax,
    int AttackSpeed,
    int AttackRange,
    int Accuracy,
    int Defence,
    int AbsorbPct,
    int BlockPct,
    int Hp,
    int Sight,
    int SkillDamageMin,
    int SkillDamageMax,
    int SkillDistance,
    int SkillRange,
    int SkillRatingPct,
    int SkillCurseCode,
    int MoveSpeed,
    int MoveRange,
    int Iq,
    int GroupMin,
    int GroupMax,
    int Exp,
    int ActiveHour,
    short Nature,
    short Brood,
    short Class,
    int ResBionic,
    int ResEarth,
    int ResFire,
    int ResIce,
    int ResLighting,
    int ResPoison,
    int ResWater,
    int ResWind,
    int StunPct,
    string DisplayName = "Monster",
    int SoundCode = 0,
    float ModelScale = 1)
{
    public MonsterDropConfigRecord? DropConfig { get; init; }

    public IReadOnlyList<MonsterDropItemRecord> DropItems { get; init; } = [];

    public IReadOnlyList<MonsterDropPlusRecord> DropsPlus { get; init; } = [];
}

public sealed record MonsterDropConfigRecord(
    string MonsterName,
    int MaxItemsPerKill,
    bool AllPlayersSee);

public sealed record MonsterDropItemRecord(
    long Id,
    string MonsterName,
    int Kind,
    int Weight,
    string? ItemCode,
    int GoldMin,
    int GoldMax);

public sealed record MonsterDropPlusRecord(
    long Id,
    string MonsterName,
    int Weight,
    string? ItemCode);
