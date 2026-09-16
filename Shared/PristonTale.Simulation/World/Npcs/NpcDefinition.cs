namespace PristonTale.Simulation.World.Npcs;

[Flags]
public enum NpcServices : uint
{
    None = 0,
    Warehouse = 1 << 0,
    Shop = 1 << 1,
    SkillMaster = 1 << 2,
    JobChange = 1 << 3,
    Teleport = 1 << 4,
    ItemMix = 1 << 5,
    ItemAging = 1 << 6,
    Smelting = 1 << 7,
    Manufacture = 1 << 8,
    MixtureReset = 1 << 9,
    Clan = 1 << 10,
    BlessCastle = 1 << 11,
    Quest = 1 << 12,
    EventBooth = 1 << 13,
    CollectMoney = 1 << 14,
    GiveMoney = 1 << 15
}

public sealed record NpcShopStock(
    IReadOnlyList<uint> WeaponItemCodes,
    IReadOnlyList<uint> ArmorItemCodes,
    IReadOnlyList<uint> MiscItemCodes)
{
    public static readonly NpcShopStock Empty = new([], [], []);
}

public sealed record NpcQuestHook(int QuestCode, int QuestParam)
{
    public static readonly NpcQuestHook None = new(0, 0);
}

public sealed record NpcDefinition(
    int Code,
    string Name,
    string ModelInxPath,
    NpcServices Services)
{
    public int JobChangeCode { get; init; }

    public int TeleportCode { get; init; }

    public NpcShopStock Shop { get; init; } = NpcShopStock.Empty;
    public NpcQuestHook Quest { get; init; } = NpcQuestHook.None;
    public IReadOnlyList<string> Dialog { get; init; } = [];

    public float ModelScale { get; init; } = 1f;

    public int SoundCode { get; init; }

    public float MoveRange { get; init; } = 64f;

    public bool Has(NpcServices s) => (Services & s) == s && s != NpcServices.None;
}
