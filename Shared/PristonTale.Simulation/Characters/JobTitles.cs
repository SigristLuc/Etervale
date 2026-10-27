using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Characters;

// The title of each class at each tier (0..3), as the classic client names them.
public static class JobTitles
{
    public static IReadOnlyList<string> For(PlayerClass characterClass) => characterClass switch
    {
        PlayerClass.Mechanician => ["Mechanician", "Mechanic Master", "Metal Leader", "Metallion"],
        PlayerClass.Fighter => ["Fighter", "Warrior", "Champion", "Immortal Warrior"],
        PlayerClass.Pikeman => ["Pikeman", "Combatant", "Lancer", "Lancelot"],
        PlayerClass.Archer => ["Archer", "Huntress Master", "Dion's Disciple", "Sagittarion"],
        PlayerClass.Knight => ["Knight", "Paladin", "Holy Knight", "SaintKnight"],
        PlayerClass.Atalanta => ["Atalanta", "Valkyrie", "Brunhild", "Valhalla"],
        PlayerClass.Priestess => ["Priestess", "Saintess", "Bishop", "Celestial"],
        PlayerClass.Magician => ["Magician", "Wizard", "RoyalWizard", "ArchMage"],
        PlayerClass.Assassin => ["Assassin", "Rogue", "Shadow", "Shadow Walker"],
        PlayerClass.Shaman => ["Shaman", "Clairvoyant", "Soul Mage", "Necromancer"],
        _ => throw new ArgumentOutOfRangeException(nameof(characterClass))
    };

    public static string At(PlayerClass characterClass, int tier) => For(characterClass)[Math.Clamp(tier, 0, 3)];
}
