namespace PristonTale.WorldImport.Parsers;

/// <summary>
/// Importer DTO for one NPC <c>.npc</c> (<see cref="NpcParser"/>). Mirrors the <c>npcs</c> row plus shop
/// stock. Service flags are booleans (folded to the Simulation NpcServices enum downstream). Shop item
/// references are RAW 5-char code strings, resolved to item_templates.code by the <see cref="Importer"/>.
/// Dialog lines (≤20) ride as a list serialised to the <c>dialog</c> jsonb column.
/// </summary>
public sealed class ParsedNpc
{
    public required string Name { get; init; }
    public string Model { get; set; } = "";
    public string LinkedFile { get; set; } = "";
    public int SoundCode { get; set; }
    public float ModelScale { get; set; } = 1f;
    public int MoveRange { get; set; } = 64;

    public bool SvcWarehouse { get; set; }
    public bool SvcShop { get; set; }       // derived: any sell list present
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

    public List<string> Dialog { get; } = [];

    // Shop stock: (tab, ordinal, raw item code). tab 0=weapon,1=armour,2=misc.
    public List<(int Tab, int Ordinal, string Code)> Shop { get; } = [];
}
