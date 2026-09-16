using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Widgets;

internal static class HudJobTitles
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
}
