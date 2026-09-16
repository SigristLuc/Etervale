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
    public static DerivedStats Compute(
        CharacterAttributes attrs,
        int level,
        CharacterFunctionProfile fn,
        ItemStatBlock items = default)
    {
        float fStr = attrs.Strength;
        float fSpi = attrs.Spirit;
        float fTal = attrs.Talent;
        float fDex = attrs.Agility; // code 'Dexterity' == our Agility
        float fHlt = attrs.Health;
        float fLevel = level;

        int attackRating = (int)(items.AttackRating + fDex * 3.1f + fLevel * 1.9f + fTal * 1.5f);

        // Defence (:6121) — float division
        int defence = (int)(fDex / 2f + fTal / 4f + fLevel * 1.4f + items.Defence);

        // Absorption (:6125-6127) — uses the just-computed Defence as a float
        int absorption = (int)(defence / 100f + fLevel / 10f + (fStr + fTal) / 40f) + 1;

        int maxWeight = (int)(fStr * 2f + fHlt * 1.5f + fLevel * 3f + 60f);

        // Move Speed (:6186-6187). NowWeight = 0 until carried-weight tracking lands.
        int moveSpeed = (int)((fTal + fHlt + fLevel + 60f) / 150f) + 1;

        // Max HP (:6196-6218) — branch on LifeFunction
        float life = fn.LifeFunction switch
        {
            1 => fLevel * 2.1f + fHlt * 2.4f + fStr * 0.8f - 10f,
            2 => fLevel * 2.1f + fHlt * 2.2f + fStr * 0.6f - 5f,
            3 => fLevel * 1.8f + fHlt * 2.1f + fStr * 0.3f,
            4 => fLevel * 1.5f + fHlt * 2.1f,
            _ => fLevel * 1.5f + fHlt * 1.9f, // 5
        };
        int maxLife = (short)life;

        // Max MP (:6275-6289) — branch on ManaFunction
        float mana = fn.ManaFunction switch
        {
            1 => fLevel * 1.5f + fSpi * 3.8f,
            2 => fLevel * 0.9f + fSpi * 2.7f,
            _ => fLevel * 0.6f + fSpi * 2.2f, // 3
        };
        int maxMana = (short)mana;

        // Max SP (:6310) — universal; (Str+Tal)/2 is float division here
        int maxStamina = (short)(fHlt * 1.4f + (fStr + fTal) / 2f + fLevel * 2.3f + 80f + fSpi);

        // Bare-hand melee damage (:6031-6037) — INTEGER division on the raw int stats. The
        // weapon-equipped path (str-scaled by the weapon damage) lands with the item track.
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
        dmgMax += 1;

        int attackSpeed = items.AttackSpeed;

        return new DerivedStats(
            maxLife, maxMana, maxStamina,
            attackRating, dmgMin, dmgMax,
            defence, absorption, moveSpeed, maxWeight, attackSpeed);
    }
}
