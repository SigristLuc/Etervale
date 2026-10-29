using PristonTale.Simulation.World.Npcs;

namespace PristonTale.WorldImport.Parsers;

// Importer DTO for one NPC `.npc` (NpcParser) and its linked name file. Services are the typed services in click
// order (the `npc_services` rows). Shop item references are raw 5-character codes, resolved by the Importer.
public sealed class ParsedNpc
{
    // The authored identity, which other files reference; the importer maps it to the NPC's id and key.
    [System.Text.Json.Serialization.JsonIgnore]
    public string Name { get; init; } = "";
    public string DisplayName { get; set; } = "";
    public int PortraitOffsetY { get; set; }
    public int PortraitOffsetZ { get; set; }
    public string Model { get; set; } = "";
    public string LinkedFile { get; set; } = "";
    public int SoundCode { get; set; }
    public float ModelScale { get; set; } = 1f;
    public int MoveRange { get; set; } = 64;
    // *속성 적 makes the character hostile; *레벨 its level; *크기 its shadow size (-1 none).
    public bool Hostile { get; set; }
    public int Level { get; set; }
    public int SizeLevel { get; set; }
    // *출현간격 slot, cycle: the NPC appears only when the field's draw modulo the cycle equals the slot (cycle 0: always).
    public int AppearSlot { get; set; }
    public int AppearCycle { get; set; }

    // Keys of the file no rule reads; the importer reports them.
    public List<string> UnknownKeys { get; } = [];

    // Every name label by stored key (NAME, E_NAME, B_NAME, J_NAME, T_NAME), and every dialog key's lines (at most 20
    // each); DialogKey names the English lines players read (E_CHAT, else B_CHAT; empty when there are none).
    public Dictionary<string, string> Labels { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> DialogLines { get; } = new(StringComparer.Ordinal);
    public string DialogKey { get; set; } = "";

    public List<NpcService> Services { get; } = [];

    // Shop stock: (tab, ordinal, raw item code). tab 0=weapon,1=armour,2=misc.
    public List<(int Tab, int Ordinal, string Code)> Shop { get; } = [];
}
