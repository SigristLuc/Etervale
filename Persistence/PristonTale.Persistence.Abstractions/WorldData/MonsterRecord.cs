namespace PristonTale.Persistence.Abstractions.WorldData;

// Id: the importer's stable monster id, also the runtime monster code; Key: its unique English name.
public sealed record MonsterRecord(
    int Id,
    string Key,
    string DisplayName,
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
    long Exp,
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
    int SoundCode = 0,
    float ModelScale = 1,
    int PortraitOffsetY = 0,
    int PortraitOffsetZ = 0,
    int QuestCode = 0,
    string EventItem = "",
    int PotionCount = 0,
    int PotionPercent = 0,
    bool Hostile = true,
    int SizeLevel = -1,
    int ResMagic = 0,
    int MoveType = 0,
    int SpecialAttackPct = 0,
    int GoldMin = 0,
    int GoldMax = 0,
    int GoldPct = 0,
    string EventModel = "",
    int EventCode = 0,
    int EventInfo = 0)
{
    public MonsterDropConfigRecord? DropConfig { get; init; }

    public IReadOnlyList<MonsterDropItemRecord> DropItems { get; init; } = [];

    public IReadOnlyList<MonsterDropPlusRecord> DropsPlus { get; init; } = [];
}

public sealed record MonsterDropConfigRecord(
    int MonsterId,
    int MaxItemsPerKill,
    bool AllPlayersSee);

public sealed record MonsterDropItemRecord(
    long Id,
    int MonsterId,
    int Kind,
    int Weight,
    string? ItemCode,
    int GoldMin,
    int GoldMax);

public sealed record MonsterDropPlusRecord(
    long Id,
    int MonsterId,
    int Weight,
    string? ItemCode);
