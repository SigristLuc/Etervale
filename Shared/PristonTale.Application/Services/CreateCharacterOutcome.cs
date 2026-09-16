namespace PristonTale.Application.Services;

public sealed record CreateCharacterOutcome(bool Ok, string Message, CharacterSummary? Character)
{
    public static CreateCharacterOutcome Failed(string message) => new(false, message, null);

    public static CreateCharacterOutcome Success(CharacterSummary character) =>
        new(true, "created", character);
}
