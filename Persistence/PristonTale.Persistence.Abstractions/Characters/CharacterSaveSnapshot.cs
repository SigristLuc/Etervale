namespace PristonTale.Persistence.Abstractions.Characters;

public sealed record CharacterSaveSnapshot(
    CharacterRecord Scalars,
    SavedItems Inventory,
    SavedItems Equipment,
    SavedSkills Skills,
    DerivedStatsDto Derived)
{
    public CharacterRecord Encode()
    {
        return Scalars with
        {
            Inventory = CharacterJson.Write(Inventory),
            Equipment = CharacterJson.Write(Equipment),
            Skills = CharacterJson.Write(Skills),
            DerivedStats = CharacterJson.Write(Derived)
        };
    }
}
