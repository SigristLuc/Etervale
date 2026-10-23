using PristonTale.Simulation.World.Npcs;

namespace PristonTale.WorldImport.Parsers;

// Importer DTO for one NPC `.npc` (NpcParser) and its linked name file. Services are the typed services in click
// order (the `npc_services` rows); the boolean flags and codes mirror the older `npcs.svc_*` columns. Shop item
// references are raw 5-character codes, resolved by the Importer.
public sealed class ParsedNpc
{
    public required string Name { get; init; }
    public string DisplayName { get; set; } = "";
    public int PortraitOffsetY { get; set; }
    public int PortraitOffsetZ { get; set; }
    public string Model { get; set; } = "";
    public string LinkedFile { get; set; } = "";
    public int SoundCode { get; set; }
    public float ModelScale { get; set; } = 1f;
    public int MoveRange { get; set; } = 64;

    public bool SvcWarehouse { get; set; }
    public bool SvcShop { get; set; }
    public bool SvcSkillMaster { get; set; }
    public bool SvcJobChange { get; set; }
    public bool SvcTeleport { get; set; }
    public bool SvcItemMix { get; set; }
    public bool SvcItemAging { get; set; }
    public bool SvcSmelting { get; set; }
    public bool SvcManufacture { get; set; }
    public bool SvcMixtureReset { get; set; }
    public bool SvcClan { get; set; }
    public bool SvcBlessCastle { get; set; }
    public bool SvcQuest { get; set; }
    public bool SvcEventBooth { get; set; }
    public bool SvcCollectMoney { get; set; }
    public bool SvcGiveMoney { get; set; }

    public int JobChangeCode { get; set; }
    public int TeleportCode { get; set; }
    public int QuestCode { get; set; }
    public int QuestParam { get; set; }

    // Dialog lines of the chosen locale (at most 20) and the key they came from.
    public List<string> Dialog { get; } = [];
    public string DialogKey { get; set; } = "";

    public List<NpcService> Services { get; } = [];

    // Shop stock: (tab, ordinal, raw item code). tab 0=weapon,1=armour,2=misc.
    public List<(int Tab, int Ordinal, string Code)> Shop { get; } = [];
}
