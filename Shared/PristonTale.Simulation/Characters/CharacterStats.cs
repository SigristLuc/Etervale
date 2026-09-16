namespace PristonTale.Simulation.Characters;

public sealed record CharacterAttributes(
    int Strength,
    int Spirit,
    int Talent,
    int Agility,
    int Health)
{
    public static readonly CharacterAttributes Empty = new(0, 0, 0, 0, 0);

    public CharacterAttributes Add(CharacterAttributes other) =>
        new(
            Strength + other.Strength,
            Spirit + other.Spirit,
            Talent + other.Talent,
            Agility + other.Agility,
            Health + other.Health);

    public CharacterAttributes Scale(int value) =>
        new(
            Strength * value,
            Spirit * value,
            Talent * value,
            Agility * value,
            Health * value);
}

public sealed record CharacterFunctionProfile(
    int Sight,
    int LifeFunction,
    int ManaFunction,
    int StaminaFunction,
    int MeleeDamageFunction,
    int RangedDamageFunction,
    int MagicDamageFunction);
