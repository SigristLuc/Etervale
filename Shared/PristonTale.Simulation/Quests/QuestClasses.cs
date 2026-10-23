using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Quests;

// Class-dependent quest facts: tribe, home town, the quest weapon of the second rank-up, the ring that rank-up gives,
// the beginner weapon and the duel master of the third rank-up.
public static class QuestClasses
{
    public const int RicartenField = 3;
    public const int PillaiField = 21;

    // The quests split the classes by class number, not by tribe: the first four take the Tempskron side (Ricarten, its
    // NPCs and monsters), every later class the Morion side, the assassin included.
    public static bool IsTempskron(PlayerCharacter character) => (int)ClassOf(character) < 5;

    // The first rank-up gathers trophies on the Tempskron side and for the assassin; the other classes brew a potion.
    public static bool RanksUpByTrophies(PlayerCharacter character) => IsTempskron(character) || ClassOf(character) == PlayerClass.Assassin;

    // The level 30 quest of the Ricarten store serves the eight classic classes.
    public static bool IsClassicClass(PlayerCharacter character) => (int)ClassOf(character) <= 8;

    public static PlayerClass ClassOf(PlayerCharacter character) => character.Definition.Class;

    public static int HomeField(PlayerCharacter character) => IsTempskron(character) ? RicartenField : PillaiField;

    // Rank-up quests run only at the skill master of the class's home town.
    public static bool IsHomeTown(PlayerCharacter character, int fieldCode) => fieldCode == HomeField(character);

    public static uint QuestWeapon(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Fighter => LegacyItemCode.Parse("WA108"),
        PlayerClass.Mechanician => LegacyItemCode.Parse("WC108"),
        PlayerClass.Archer => LegacyItemCode.Parse("WS110"),
        PlayerClass.Pikeman => LegacyItemCode.Parse("WP109"),
        PlayerClass.Atalanta => LegacyItemCode.Parse("WT109"),
        PlayerClass.Knight => LegacyItemCode.Parse("WS210"),
        PlayerClass.Magician or PlayerClass.Priestess => LegacyItemCode.Parse("WM109"),
        PlayerClass.Assassin => CodeOrZero("WD109"),
        PlayerClass.Shaman => CodeOrZero("WN109"),
        _ => 0
    };

    // Dagger and phantom codes resolve only once the item codes cover their categories.
    private static uint CodeOrZero(string code) => LegacyItemCode.TryParse(code, out var value) ? value : 0;

    // How the quest weapon counts kills while it grows.
    public static QuestWeaponHunt WeaponHunt(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Archer or PlayerClass.Atalanta => QuestWeaponHunt.Ranged,
        PlayerClass.Magician or PlayerClass.Priestess or PlayerClass.Shaman => QuestWeaponHunt.Field,
        _ => QuestWeaponHunt.Melee
    };

    public static uint RankUpRing(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Magician or PlayerClass.Priestess => QuestItems.ArcaneRing,
        PlayerClass.Archer or PlayerClass.Atalanta => QuestItems.FocusRing,
        _ => QuestItems.EmperorRing
    };

    // The first beginner quest's weapon, specialised for the class.
    public static uint BeginnerWeapon(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Mechanician => LegacyItemCode.Parse("WC102"),
        PlayerClass.Fighter => LegacyItemCode.Parse("WA102"),
        PlayerClass.Pikeman => LegacyItemCode.Parse("WP102"),
        PlayerClass.Archer => LegacyItemCode.Parse("WS102"),
        PlayerClass.Knight => LegacyItemCode.Parse("WS202"),
        PlayerClass.Atalanta => LegacyItemCode.Parse("WT102"),
        PlayerClass.Priestess or PlayerClass.Magician => LegacyItemCode.Parse("WM101"),
        _ => 0
    };

    public static uint JobMask(PlayerCharacter character) => (uint)character.Definition.JobBitCode;

    // The master the third rank-up duels: the Tempskron classes face the skill master, the Morion ones the magic master.
    public static int DuelMasterSound(PlayerCharacter character) =>
        IsTempskron(character) ? World.Npcs.LegacyNpcSoundCode.SkillMaster : World.Npcs.LegacyNpcSoundCode.MagicMaster;
}

public enum QuestWeaponHunt : byte
{
    // Kills of one monster per stage: Bargon, Skeleton Warrior, Head Cutter.
    Melee = 0,

    // Kills of one monster per stage: Armored Beetle, Skeleton Ranger, Titan.
    Ranged = 1,

    // Kills of any monster in one field per stage.
    Field = 2
}
