using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Items;

// Whether a character can use an item copy: level and the five attribute requirements of the copy, and the class and
// body restrictions of its kind. An equipped item that stops meeting them stays worn but adds nothing.
public static class ItemUsability
{
    public static bool MeetsRequirements(PlayerCharacter character, ItemInstance item)
    {
        var roll = item.Roll;
        var attributes = character.Attributes;
        var requirements = roll.Requirements;
        return character.Level >= roll.RequiredLevel
            && attributes.Strength >= requirements.Strength
            && attributes.Spirit >= requirements.Spirit
            && attributes.Talent >= requirements.Talent
            && attributes.Agility >= requirements.Agility
            && attributes.Health >= requirements.Health;
    }

    public static bool ClassAllowed(PlayerCharacter character, ItemDefinition definition) =>
        definition.AllowedJobs == 0 || (definition.AllowedJobs & (uint)character.Definition.JobBitCode) != 0;

    public static bool CanUse(PlayerCharacter character, ItemInstance item) =>
        MeetsRequirements(character, item) && ClassAllowed(character, item.Definition);
}
