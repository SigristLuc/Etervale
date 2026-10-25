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
    float MagicMastery = 0,
    // Inventory cells, measured by the importer from the item's inventory sprite.
    int GridWidth = 1,
    int GridHeight = 1,
    // The glow of a unique weapon: colour 0..255, blink period in ticks and scale pulse.
    int GlowR = 0,
    int GlowG = 0,
    int GlowB = 0,
    int GlowA = 0,
    int GlowBlink = 0,
    float GlowScale = 0,
    int DisplayEffect = 0,
    // Drops of the item per day across the server (0 unlimited); its set and the pieces worn for the set bonus.
    int DailyDropLimit = 0,
    int SetCode = 0,
    int SetPieces = 0)
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
