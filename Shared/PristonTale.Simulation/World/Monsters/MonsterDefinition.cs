using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Simulation.World.Monsters;

public enum MonsterFaction
{
    Npc = 0,
    Enemy = 1
}

public enum MonsterBrood
{
    Normal = 0x00,
    Undead = 0x90,
    Mutant = 0x91,
    Demon = 0x92,
    Mechanic = 0x93,
    User = 0xA0
}

public enum MonsterNature
{
    Natural = 0x80,
    Good = 0x81,
    Evil = 0x82
}

public enum MonsterClass
{
    Normal = 0,
    Boss = 1,
    Hammer = 200,
    Ghost = 300
}

public enum MonsterActiveHour
{
    Any = 0,
    Day = 1,
    Night = -1
}

public sealed record MonsterRange(int Min, int Max)
{
    public static readonly MonsterRange Zero = new(0, 0);
}

public sealed record MonsterCombatStats(
    int Level,
    int AttackRatingAccuracy,
    MonsterRange AttackDamage,
    int AttackSpeed,
    int MeleeRange,
    int Defence,
    int Absorption,
    int BlockChance,
    int MaxLife,
    ResistanceBlock Resistances)
{
    public int DamageStunPercent { get; init; } = 100;
}

public sealed record MonsterSkillStats(
    MonsterRange SkillDamage,
    int SkillRange,
    int SkillDistance,
    int SkillRatingPercent,
    int CurseSkillCode)
{
    public static readonly MonsterSkillStats None = new(MonsterRange.Zero, 0, 0, 0, 0);
}

public sealed record MonsterAi(
    int Iq,
    MonsterNature Nature,
    int SightRadius,
    int MoveSpeed,
    int MoveRange,
    MonsterRange GroupSize,
    MonsterActiveHour ActiveHour)
{
    public static readonly MonsterAi Default =
        new(0, MonsterNature.Natural, 0, 0, 0, MonsterRange.Zero, MonsterActiveHour.Any);
}

public sealed record MonsterDefinition(
    int Code,
    string Name,
    MonsterFaction Faction,
    MonsterClass Class,
    MonsterBrood Brood,
    MonsterCombatStats Combat,
    string ModelInxPath)
{
    public MonsterSkillStats Skill { get; init; } = MonsterSkillStats.None;
    public MonsterAi Ai { get; init; } = MonsterAi.Default;
    public DropTable Drops { get; init; } = DropTable.Empty;

    public int ExpReward { get; init; }

    public float ModelScale { get; init; } = 1f;

    public int SoundCode { get; init; }

    public int ShadowSizeLevel { get; init; }

    public bool IsBoss => Class == MonsterClass.Boss;
    public bool IsHostile => Faction == MonsterFaction.Enemy;
}
