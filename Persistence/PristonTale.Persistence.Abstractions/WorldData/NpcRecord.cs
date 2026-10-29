namespace PristonTale.Persistence.Abstractions.WorldData;

// One NPC definition file. Id: the importer's stable NPC id, also the runtime NPC code; Key: its unique English key;
// Name: its English name (npc_texts), null without one; Dialog: its English lines (npc_dialog_lines).
public sealed record NpcRecord(
    int Id,
    string Key,
    string? Name,
    string Model,
    int Level,
    bool Hostile,
    int SizeLevel,
    int MoveRange,
    float ModelScale,
    int SoundCode,
    int PortraitOffsetY,
    int PortraitOffsetZ,
    int AppearSlot,
    int AppearCycle)
{
    public IReadOnlyList<NpcShopItemRecord> ShopItems { get; init; } = [];

    public IReadOnlyList<NpcServiceRecord> Services { get; init; } = [];

    public IReadOnlyList<string> Dialog { get; init; } = [];
}

// Tab 0 weapons, 1 armour, 2 goods.
public sealed record NpcShopItemRecord(
    int NpcId,
    int Tab,
    int Ordinal,
    string ItemCode);

// Kind is a PristonTale.Simulation.World.Npcs.NpcServiceKind value; Ordinal is the service's order on a click.
public sealed record NpcServiceRecord(
    int NpcId,
    int Ordinal,
    int Kind,
    int Value,
    int Value2,
    string Text);

public sealed record NpcDialogLineRecord(
    int NpcId,
    int Ordinal,
    string Text);
