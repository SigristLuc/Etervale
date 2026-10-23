using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Abstractions.WorldData;

// The svc_* flags and their codes predate npc_services; the importer still fills them from the typed services, and the
// runtime reads them only for databases imported before npc_services existed.
public sealed record NpcRecord(
    string Name,
    string Model,
    string LinkedFile,
    bool SvcWarehouse,
    bool SvcShop,
    bool SvcSkillMaster,
    bool SvcJobChange,
    bool SvcTeleport,
    bool SvcItemMix,
    bool SvcItemAging,
    bool SvcSmelting,
    bool SvcManufacture,
    bool SvcMixtureReset,
    bool SvcClan,
    bool SvcBlessCastle,
    bool SvcQuest,
    bool SvcEventBooth,
    bool SvcCollectMoney,
    bool SvcGiveMoney,
    int JobChangeCode,
    int TeleportCode,
    int QuestCode,
    int QuestParam,
    JsonText Dialog,
    int SoundCode = 0,
    int MoveRange = 64,
    float ModelScale = 1,
    string DisplayName = "",
    int PortraitOffsetY = 0,
    int PortraitOffsetZ = 0)
{
    public IReadOnlyList<NpcShopStockRecord> ShopStock { get; init; } = [];

    public IReadOnlyList<NpcServiceRecord> Services { get; init; } = [];
}

public sealed record NpcShopStockRecord(
    long Id,
    string NpcName,
    int Tab,
    int Ordinal,
    string ItemCode);

// Kind is a PristonTale.Simulation.World.Npcs.NpcServiceKind value; Ordinal is the service's order on a click.
public sealed record NpcServiceRecord(
    long Id,
    string NpcName,
    int Ordinal,
    int Kind,
    int Value,
    int Value2,
    string Text);
