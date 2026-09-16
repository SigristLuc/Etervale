using PristonTale.Simulation.Items;

namespace PristonTale.WorldImport.Parsers;

/// <summary>
/// The importer DTO produced by <see cref="ItemTxtParser"/> for one OpenItem <c>.txt</c>. It carries the
/// parsed scalar fields plus the variadic <c>**특화랜덤</c> job list and <c>**</c> special bonuses. The
/// <see cref="Importer"/> turns this into an <c>item_templates</c> row (+ child rows), deriving the
/// <c>item_code_mask</c> from <see cref="Code"/> using the original category codes.
/// </summary>
public sealed class ParsedItem
{
    public required string Code { get; init; }     // 5-char LastCategory, the natural PK
    public required string Name { get; init; }
    public ItemKind Kind { get; init; }
    public long UniqueItem { get; init; }
    public int MaxStack { get; init; } = 1;
    public int PotionSpace { get; init; }
    public float MagicMastery { get; init; }

    public int DurabilityMin { get; init; }
    public int DurabilityMax { get; init; }
    public int Weight { get; init; }
    public int Price { get; init; }

    // damage[4] = minLow, minHigh, maxLow, maxHigh.
    public int DamageMinLow { get; init; }
    public int DamageMinHigh { get; init; }
    public int DamageMaxLow { get; init; }
    public int DamageMaxHigh { get; init; }

    public int AttackRatingMin { get; init; }
    public int AttackRatingMax { get; init; }
    public int CriticalHit { get; init; }
    public int DefenceMin { get; init; }
    public int DefenceMax { get; init; }
    public float BlockMin { get; init; }
    public float BlockMax { get; init; }
    public float AbsorbMin { get; init; }
    public float AbsorbMax { get; init; }
    public float SpeedMin { get; init; }
    public float SpeedMax { get; init; }

    public float ManaRegenMin { get; init; }
    public float ManaRegenMax { get; init; }
    public float LifeRegenMin { get; init; }
    public float LifeRegenMax { get; init; }
    public float StaminaRegenMin { get; init; }
    public float StaminaRegenMax { get; init; }

    public int AddManaMin { get; init; }
    public int AddManaMax { get; init; }
    public int AddLifeMin { get; init; }
    public int AddLifeMax { get; init; }
    public int AddStaminaMin { get; init; }
    public int AddStaminaMax { get; init; }

    public int ReqLevel { get; init; }
    public int ReqStrength { get; init; }
    public int ReqSpirit { get; init; }
    public int ReqTalent { get; init; }
    public int ReqDexterity { get; init; }
    public int ReqHealth { get; init; }

    public int RestoreStaminaMin { get; init; }
    public int RestoreStaminaMax { get; init; }
    public int RestoreManaMin { get; init; }
    public int RestoreManaMax { get; init; }
    public int RestoreLifeMin { get; init; }
    public int RestoreLifeMax { get; init; }

    public int ShootingRange { get; init; }
    public int AttackSpeed { get; init; }
    public long JobMask { get; init; }

    // Eight resistance min/max pairs in ResistanceBlock order (bionic..wind).
    public int[] ResMin { get; } = new int[8];
    public int[] ResMax { get; } = new int[8];

    // Variadic children.
    public List<long> JobRandom { get; } = [];                  // **특화랜덤 job bitcodes
    public List<(int Code, float V0, float V1)> Specials { get; } = []; // ** special bonuses
}
