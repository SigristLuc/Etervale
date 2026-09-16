using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Skills;

[Flags]
public enum SkillMouseUse
{
    None = 0,
    Left = 1,
    Right = 2,
    Both = Left | Right
}

public enum JobTier
{
    Job1 = 0x1,
    Job2 = 0x2,
    Job3 = 0x4,
    Job4 = 0x8
}

public enum SkillCategory
{
    ActiveAttack,
    AreaAttack,
    MultiHit,
    Buff,
    Passive,
    Heal,
    Summon,
    Debuff
}

public delegate void SkillEffect(PlayerCharacter caster);

public sealed class Skill
{
    public uint Code { get; init; }
    public string Description { get; init; } = "";
    public string IconKey { get; init; } = "";
    public SkillMouseUse MouseUse { get; init; }
    public IReadOnlyList<int> ManaByRank { get; init; } = Array.Empty<int>();
    public int StaminaBase { get; init; }
    public int StaminaPerRank { get; init; }
    public uint AllowedWeaponsMask { get; init; }
    public required string Name { get; init; }
    public required JobTier Tier { get; init; }
    public required SkillCategory Category { get; init; }

    public int RequiredLevel { get; init; }
    public int ManaCost { get; init; }
    public float CooldownSeconds { get; init; }

    public SkillEffect Effect { get; init; }

    public bool IsActive => Category is SkillCategory.ActiveAttack or SkillCategory.AreaAttack
        or SkillCategory.MultiHit or SkillCategory.Debuff;

    public bool CanCast(PlayerCharacter caster) =>
        caster.Level >= RequiredLevel && caster.Mana >= ManaCost;

    public int ManaForRank(int rank)
    {
        ValidateRank(rank);
        return ManaByRank[rank - 1];
    }

    public int StaminaForRank(int rank)
    {
        ValidateRank(rank);
        return StaminaBase + StaminaPerRank * (rank - 1);
    }

    public bool AllowsWeapon(ItemKind kind)
    {
        return Enum.IsDefined(kind) && (AllowedWeaponsMask == 0 || (AllowedWeaponsMask & (1u << (int)kind)) != 0);
    }

    private void ValidateRank(int rank)
    {
        if (rank is < 1 or > 10 || ManaByRank.Count != 10)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), "A migrated skill rank must be between 1 and 10.");
        }
    }

    public static int RequiredLevelFor(JobTier tier) => tier switch
    {
        JobTier.Job1 => 9,
        JobTier.Job2 => 19,
        JobTier.Job3 => 39,
        JobTier.Job4 => 59,
        _ => 1
    };

    public static Skill Of(string name, JobTier tier, SkillCategory category, string iconKey = "") => new()
    {
        Name = name,
        IconKey = iconKey,
        Tier = tier,
        Category = category,
        RequiredLevel = RequiredLevelFor(tier)
    };
}
