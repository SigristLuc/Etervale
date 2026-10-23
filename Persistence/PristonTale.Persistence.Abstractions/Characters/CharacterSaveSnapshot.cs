namespace PristonTale.Persistence.Abstractions.Characters;

public sealed record CharacterSaveSnapshot(
    CharacterRecord Scalars,
    SavedItems Inventory,
    SavedItems Equipment,
    SavedSkills Skills,
    DerivedStatsDto Derived,
    SavedQuests? Quests = null)
{
    public CharacterRecord Encode()
    {
        return Scalars with
        {
            Inventory = CharacterJson.Write(Inventory),
            Equipment = CharacterJson.Write(Equipment),
            Skills = CharacterJson.Write(Skills),
            Quests = Quests is null ? Scalars.Quests : CharacterJson.Write(Quests),
            DerivedStats = CharacterJson.Write(Derived)
        };
    }
}
