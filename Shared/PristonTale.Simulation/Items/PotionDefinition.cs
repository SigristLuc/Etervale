using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Simulation.Items;

[Flags]
public enum PotionResource
{
    None = 0,
    Life = 1,
    Mana = 2,
    Stamina = 4
}

public sealed record PotionDefinition(int LifeMin, int LifeMax, int ManaMin, int ManaMax, int StaminaMin, int StaminaMax)
{
    public bool IsValid => LifeMin >= 0 && LifeMax >= LifeMin
        && ManaMin >= 0 && ManaMax >= ManaMin
        && StaminaMin >= 0 && StaminaMax >= StaminaMin
        && (LifeMax > 0 || ManaMax > 0 || StaminaMax > 0);

    public PotionResource Restore(PlayerCharacter character, ISimRng random)
    {
        if (!IsValid || character.Life <= 0)
        {
            return PotionResource.None;
        }

        var resources = PotionResource.None;
        if (LifeMax > 0)
        {
            character.Life = RestoreValue(character.Life, character.MaxLife, LifeMin, LifeMax, random);
            resources |= PotionResource.Life;
        }

        if (ManaMax > 0)
        {
            character.Mana = RestoreValue(character.Mana, character.MaxMana, ManaMin, ManaMax, random);
            resources |= PotionResource.Mana;
        }

        if (StaminaMax > 0)
        {
            character.Stamina = RestoreValue(character.Stamina, character.MaxStamina, StaminaMin, StaminaMax, random);
            resources |= PotionResource.Stamina;
        }

        return resources;
    }

    private static int RestoreValue(int current, int maximum, int minimum, int upper, ISimRng random)
    {
        var amount = upper == minimum ? minimum : random.NextExclusive(minimum, upper);
        return (int)Math.Min(maximum, (long)current + amount);
    }
}
