using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Characters;

public readonly record struct DerivedStats(
    int MaxLife,
    int MaxMana,
    int MaxStamina,
    int AttackRating,
    int AttackDamageMin,
    int AttackDamageMax,
    int Defence,
    int Absorption,
    int MoveSpeed,
    int MaxWeight,
    int AttackSpeed)
{
    // Combat values the equipment adds; skill and buff modifiers are applied where these are used.
    public int CriticalChance { get; init; }
    public int BlockChance { get; init; }
    public int ShootingRange { get; init; }
    public ResistanceBlock Resistances { get; init; }

    // Regeneration in points per second added to the natural rate, as items rate it.
    public float LifeRegen { get; init; }
    public float ManaRegen { get; init; }
    public float StaminaRegen { get; init; }
    public float MagicMastery { get; init; }
    public int PotionSpace { get; init; } = DefaultPotionSpace;
    public int CarriedWeight { get; init; }

    public const int DefaultPotionSpace = 2;
    public const int MaxCriticalChance = 50;

    public static DerivedStats Compute(CharacterAttributes attrs, int level, CharacterFunctionProfile fn) =>
        Compute(attrs, level, fn, EquipmentTotals.None, 0);

    public static DerivedStats Compute(
        CharacterAttributes attrs,
        int level,
        CharacterFunctionProfile fn,
        EquipmentTotals items,
        int carriedWeight)
    {
        float fStr = attrs.Strength;
        float fSpi = attrs.Spirit;
        float fTal = attrs.Talent;
        float fDex = attrs.Agility; // the classic Dexterity stat
        float fHlt = attrs.Health;
        float fLevel = level;

        int attackRating = (int)(items.AttackRating + fDex * 3.1f + fLevel * 1.9f + fTal * 1.5f) + items.LevelAttackRating;

        // Defence — float division
        int defence = (int)(fDex / 2f + fTal / 4f + fLevel * 1.4f + items.Defence);

        // Absorption — uses the just-computed Defence as a float
        int absorption = (int)(defence / 100f + fLevel / 10f + (fStr + fTal) / 40f) + 1;

        int maxWeight = (int)(fStr * 2f + fHlt * 1.5f + fLevel * 3f + 60f);

        // Move speed. Carried weight is not tracked, so the formula has no weight penalty.
        int moveSpeed = (int)((fTal + fHlt + fLevel + 60f) / 150f) + 1;

        // Max HP — branch on LifeFunction
        float life = fn.LifeFunction switch
        {
            1 => fLevel * 2.1f + fHlt * 2.4f + fStr * 0.8f - 10f,
            2 => fLevel * 2.1f + fHlt * 2.2f + fStr * 0.6f - 5f,
            3 => fLevel * 1.8f + fHlt * 2.1f + fStr * 0.3f,
            4 => fLevel * 1.5f + fHlt * 2.1f,
            _ => fLevel * 1.5f + fHlt * 1.9f, // 5
        };
        int maxLife = (short)life;
        maxLife += (short)(items.LevelLife + items.AddLife);

        // Max MP — branch on ManaFunction
        float mana = fn.ManaFunction switch
        {
            1 => fLevel * 1.5f + fSpi * 3.8f,
            2 => fLevel * 0.9f + fSpi * 2.7f,
            _ => fLevel * 0.6f + fSpi * 2.2f, // 3
        };
        int maxMana = (short)mana;
        // The first mana function gains only the added mana, not the level bonus of a specialization.
        maxMana += (short)(fn.ManaFunction == 1 ? items.AddMana : items.LevelMana + items.AddMana);

        // Max SP — universal; (Str+Tal)/2 is float division here
        int maxStamina = (short)(fHlt * 1.4f + (fStr + fTal) / 2f + fLevel * 2.3f + 80f + fSpi);
        maxStamina += (short)items.AddStamina;

        // Bare-hand melee damage — integer division on the raw int stats. WeaponDamage replaces it when a
        // weapon is equipped.
        int str = attrs.Strength, tal = attrs.Talent, dex = attrs.Agility;
        int dmgMin, dmgMax;
        if (fn.MeleeDamageFunction == 1)
        {
            dmgMin = 1 + (str + 130) / 130 + (tal + dex) / 40;
            dmgMax = 2 + (str + 130) / 130 + (tal + dex) / 35;
        }
        else
        {
            dmgMin = 1 + (str + 200) / 200 + (tal + dex) / 50;
            dmgMax = 2 + (str + 200) / 200 + (tal + dex) / 45;
        }
        dmgMin += 1;
        dmgMax += 1 + items.LevelDamage;

        int attackSpeed = items.AttackSpeed;

        return new DerivedStats(
            maxLife, maxMana, maxStamina,
            attackRating, dmgMin, dmgMax,
            defence, absorption, moveSpeed, maxWeight, attackSpeed)
        {
            CriticalChance = Math.Min(MaxCriticalChance, items.Critical),
            BlockChance = (int)items.Block,
            ShootingRange = items.ShootingRange,
            Resistances = items.Resistances,
            LifeRegen = items.LifeRegen,
            ManaRegen = items.ManaRegen,
            StaminaRegen = items.StaminaRegen,
            MagicMastery = items.MagicMastery,
            PotionSpace = items.PotionSpace != 0 ? items.PotionSpace : DefaultPotionSpace,
            CarriedWeight = carriedWeight
        };
    }
}
