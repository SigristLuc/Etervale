namespace PristonTale.WorldImport;

// The names the supplied data gives only in Portuguese, in English as Etervale shows every name. A monster's label is
// keyed by its file (variants of one identity are labelled apart), an item's name by its code, an NPC's label by its
// identity. Variants of a monster the original set also has take that monster's English label verbatim.
internal static class EnglishNames
{
    private static readonly Dictionary<string, string> Monsters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["38_TP_Boss_GladiadorVermelho.inf"] = "Red Gladiator",
        ["40_ET1_Boss_HoboGolem.inf"] = "Deadly Machine",
        ["43_ET3_BOSS_DevilShire.inf"] = "Demonic Shy",
        ["DVM_LavaGolem.inf"] = "Lava Golem",
        ["Event_Melancia.inf"] = "Wevyn, the Hunter",
        ["Cristal_Cao_Infernal.inf"] = "Hell Hound",
        ["Cristal_DarkSpecter.inf"] = "Dark Specter",
        ["Cristal_Duende_Gelo.inf"] = "icegoblin",
        ["Cristal_HeadCutter.inf"] = "Head Cutter",
        ["Cristal_Hobgoblin.inf"] = "Hobgoblin",
        ["Cristal_Hopy.inf"] = "HOPY",
        ["Cristal_Igolation.inf"] = "Igolation",
        ["Cristal_KingHopy.inf"] = "Hopy King",
        ["Cristal_KingHopy2.inf"] = "Horropion",
        ["Cristal_Valento.inf"] = "Valento",
        ["Cristal_Web2.inf"] = "Web"
    };

    private static readonly Dictionary<string, string> Items = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BI142"] = "Summon Blacksmith",
        ["BI226"] = "Superior Box (30d)",
        ["DA130"] = "Imperial Armor",
        ["DA230"] = "Imperial Robe",
        ["DB129"] = "Cronus Boots",
        ["DG129"] = "Cronus Gauntlets",
        ["DG131"] = "Hell Gauntlets",
        ["DG132"] = "Guardian Gauntlets",
        ["DS128"] = "Cronus Shield",
        ["DS129"] = "Angel Shield",
        ["DS130"] = "Stygian Shield",
        ["GP114"] = "Mercenary",
        ["GP115"] = "Ricarten Guard",
        ["GP116"] = "Imperial Guard",
        ["OA127"] = "Phantom Amulet",
        ["OA128"] = "Phantom Amulet +1",
        ["OA131"] = "Throne Amulet",
        ["OA132"] = "Enigma Amulet",
        ["OA134"] = "Enigma Amulet +2",
        ["OA135"] = "Enigma Amulet +3",
        ["OA136"] = "Voracious Amulet",
        ["OM127"] = "Rosary Orbital",
        ["OM130"] = "Miranda Orbital",
        ["OS129"] = "Magic Celesto",
        ["OS130"] = "Magic Mirage",
        ["OS131"] = "Magic Inferna",
        ["OR127"] = "Phantom Ring",
        ["OR128"] = "Phantom Ring +1",
        ["OR130"] = "Phantom Ring +3",
        ["OR131"] = "Throne Ring",
        ["OR132"] = "Enigma Ring",
        ["OR133"] = "Enigma Ring +1",
        ["OR134"] = "Enigma Ring +1",
        ["OR135"] = "Enigma Ring +3",
        ["OR136"] = "Voracious Ring",
        // Its *Name lost its text ("?"); the English name is its B_NAME.
        ["QT105"] = "Mysterious Oil",
        ["WA126"] = "Dragon Axe",
        ["WA131"] = "Deadly Axe",
        ["WC125"] = "Furious Talon",
        ["WC130"] = "Diabolic Talon",
        ["WH127"] = "Dragon Hammer",
        ["WH132"] = "Ruby Hammer",
        ["WM127"] = "Astral Staff",
        ["WM132"] = "Deadly Staff",
        ["WT126"] = "Blessed Javelin",
        ["WT131"] = "Chaotic Javelin",
        ["WS127"] = "Rogue Bow",
        ["WS132"] = "Deadly Bow",
        ["WS133"] = "Sinister Bow",
        ["WS134"] = "Halloween Bow 2M",
        ["WS229"] = "Mirage Sword",
        ["WS234"] = "Deadly Sword",
        ["WS235"] = "Draxos Sword"
    };

    private static readonly Dictionary<string, string> Npcs = new(StringComparer.Ordinal)
    {
        ["리본 호피"] = "Ribbon Hopy"
    };

    public static string Monster(string path, string label) => Monsters.GetValueOrDefault(Path.GetFileName(path), label);

    public static string Item(string code, string name) => Items.GetValueOrDefault(code, name);

    public static string Npc(string identity, string label) => Npcs.GetValueOrDefault(identity.Trim(), label);
}
