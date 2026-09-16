namespace PristonTale.Persistence.Abstractions.WorldData;

public sealed record ItemTemplateRecord(
    string Code,
    long ItemCodeMask,
    string Name,
    int Kind,
    int DurabilityMin,
    int DurabilityMax,
    int Weight,
    int Price,
    int DamageMinLow,
    int DamageMinHigh,
    int DamageMaxLow,
    int DamageMaxHigh,
    int AttackRatingMin,
    int AttackRatingMax,
    int CriticalHit,
    int DefenceMin,
    int DefenceMax,
    float BlockMin,
    float BlockMax,
    float AbsorbMin,
    float AbsorbMax,
    float SpeedMin,
    float SpeedMax,
    float ManaRegenMin,
    float ManaRegenMax,
    float LifeRegenMin,
    float LifeRegenMax,
    float StaminaRegenMin,
    float StaminaRegenMax,
    int AddManaMin,
    int AddManaMax,
    int AddLifeMin,
    int AddLifeMax,
    int AddStaminaMin,
    int AddStaminaMax,
    int ReqLevel,
    int ReqStrength,
    int ReqSpirit,
    int ReqTalent,
    int ReqDexterity,
    int ReqHealth,
    int RestoreStaminaMin,
    int RestoreStaminaMax,
    int RestoreManaMin,
    int RestoreManaMax,
    int RestoreLifeMin,
    int RestoreLifeMax,
    int ShootingRange,
    int AttackSpeed,
    long JobMask,
    long UniqueItem,
    // Resistance min/max per element, in ResistanceBlock order (bionic..wind).
    int ResBionicMin, int ResBionicMax,
    int ResEarthMin, int ResEarthMax,
    int ResFireMin, int ResFireMax,
    int ResIceMin, int ResIceMax,
    int ResLightingMin, int ResLightingMax,
    int ResPoisonMin, int ResPoisonMax,
    int ResWaterMin, int ResWaterMax,
    int ResWindMin, int ResWindMax,
    int MaxStack = 1,
    int PotionSpace = 0,
    float MagicMastery = 0)
{
    public IReadOnlyList<ItemTemplateSpecialRecord> Specials { get; init; } = [];

    public IReadOnlyList<ItemTemplateJobRandomRecord> JobRandom { get; init; } = [];
}

public sealed record ItemTemplateSpecialRecord(
    long Id,
    string ItemCode,
    int BonusCode,
    float Value0,
    float Value1);

public sealed record ItemTemplateJobRandomRecord(
    long Id,
    string ItemCode,
    int Ordinal,
    long JobBitCode);
