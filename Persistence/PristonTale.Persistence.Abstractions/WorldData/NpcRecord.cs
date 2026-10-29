namespace PristonTale.Persistence.Abstractions.WorldData;

// Id: the importer's stable NPC id, also the runtime NPC code; Key: its unique English name. DialogKey names the
// dialog lines players read (the English lines, E_CHAT, else B_CHAT; empty when the NPC has none).
public sealed record NpcRecord(
    int Id,
    string Key,
    string DisplayName,
    string Model,
    string LinkedFile,
    string DialogKey,
    int SoundCode,
    int MoveRange,
    float ModelScale,
    int PortraitOffsetY,
    int PortraitOffsetZ,
    bool Hostile,
    int Level,
    int SizeLevel,
    int AppearSlot,
    int AppearCycle)
{
    public IReadOnlyList<NpcShopStockRecord> ShopStock { get; init; } = [];

    public IReadOnlyList<NpcServiceRecord> Services { get; init; } = [];

    public IReadOnlyList<string> Dialog { get; init; } = [];
}

public sealed record NpcShopStockRecord(
    long Id,
    int NpcId,
    int Tab,
    int Ordinal,
    string ItemCode);

// Kind is a PristonTale.Simulation.World.Npcs.NpcServiceKind value; Ordinal is the service's order on a click.
public sealed record NpcServiceRecord(
    long Id,
    int NpcId,
    int Ordinal,
    int Kind,
    int Value,
    int Value2,
    string Text);

public sealed record NpcDialogLineRecord(
    int NpcId,
    string SourceKey,
    int Ordinal,
    string Text);
