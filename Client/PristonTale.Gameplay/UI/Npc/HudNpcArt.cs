using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Npc;

// Asset-relative paths of the original shop and warehouse window images.
public static class HudNpcArt
{
    private const string Shop = "image/Sinimage/shopall/";

    public const string Window = Shop + "shop-1.bmp";
    public const string Gold = Shop + "my_money.bmp";
    public const string Weight = Shop + "weight.bmp";
    public const string WarehouseTitle = Shop + "Warehouse.bmp";
    public const string TitleEquipment = Shop + "title-shop.bmp";
    public const string TitleGoods = Shop + "title-TabEtc.bmp";
    public const string TabWeapons = Shop + "TabAttack.bmp";
    public const string TabWeaponsIdle = Shop + "TabAttack0.bmp";
    public const string TabArmor = Shop + "TabDefense.bmp";
    public const string TabArmorIdle = Shop + "TabDefense0.bmp";
    public const string TabGoods = Shop + "TabEtc.bmp";
    public const string Buy = Shop + "Buy.bmp";
    public const string BuyIdle = Shop + "Buy0.bmp";
    public const string Sell = Shop + "Sell.bmp";
    public const string SellIdle = Shop + "Sell0.bmp";
    public const string Repair = Shop + "Repair.bmp";
    public const string RepairIdle = Shop + "DefaultRepair.bmp";
    public const string RepairAll = Shop + "AllRepair.bmp";
    public const string RepairAllIdle = Shop + "DefaultAllRepair.bmp";
    public const string BuyInfo = Shop + "ButtonInfo/BuyInfo.bmp";
    public const string SellInfo = Shop + "ButtonInfo/SellInfo.bmp";
    public const string RepairInfo = Shop + "ButtonInfo/RepairInfo.bmp";
    public const string RepairAllInfo = Shop + "ButtonInfo/RepairAllInfo.bmp";
    public const string LeftArrow = Shop + "LeftArrow.bmp";
    public const string RightArrow = Shop + "RightArrow.bmp";
    public const string Exit = Shop + "ButtonExit.bmp";
    public const string Grid = Shop + "Grid.bmp";
    public const string NoUse = Shop + "NoUseBox.bmp";

    public static readonly string[] All =
    [
        Window, Gold, Weight, WarehouseTitle, TitleEquipment, TitleGoods, TabWeapons, TabWeaponsIdle, TabArmor, TabArmorIdle, TabGoods,
        Buy, BuyIdle, Sell, SellIdle, Repair, RepairIdle, RepairAll, RepairAllIdle, BuyInfo, SellInfo, RepairInfo, RepairAllInfo,
        LeftArrow, RightArrow, Exit, Grid, NoUse
    ];

    public static string Label(NpcServiceKind kind, int value) => kind switch
    {
        NpcServiceKind.Shop => "Shop",
        NpcServiceKind.SkillMaster => "Skill training",
        NpcServiceKind.Warehouse => "Warehouse",
        NpcServiceKind.QuestGiver or NpcServiceKind.QuestScript => "Quest",
        NpcServiceKind.QuestEvent => value == Simulation.Items.Usable.Puzzles.CollectorEvent ? "Puzzle exchange" : "Event quest",
        NpcServiceKind.WarpGate => "Warp gate",
        NpcServiceKind.Teleporter => "Teleport",
        NpcServiceKind.ItemAging => "Aging",
        NpcServiceKind.ItemMix => value == 200 ? "Alchemy" : "Mixing",
        NpcServiceKind.Smelting => "Smelting",
        NpcServiceKind.Manufacture => "Crafting",
        NpcServiceKind.MixtureReset => "Mixture reset",
        NpcServiceKind.Clan => "Clan",
        NpcServiceKind.BlessCastle => "Bless Castle",
        NpcServiceKind.EventBooth => "Event",
        NpcServiceKind.EventGift => "Prize draw",
        NpcServiceKind.GiftExpress => "Item delivery",
        NpcServiceKind.StarPoint => "Star points",
        NpcServiceKind.CollectMoney => "Donations",
        NpcServiceKind.GiveMoney => "Donation box",
        NpcServiceKind.Polling => "Survey",
        NpcServiceKind.Media => "Broadcast",
        NpcServiceKind.EventMonster => "Event",
        _ => "Service"
    };
}
