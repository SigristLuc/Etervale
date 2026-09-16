using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Abstractions.WorldData;

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
    float ModelScale = 1)
{
    public IReadOnlyList<NpcShopStockRecord> ShopStock { get; init; } = [];
}

public sealed record NpcShopStockRecord(
    long Id,
    string NpcName,
    int Tab,
    int Ordinal,
    string ItemCode);
