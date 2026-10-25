namespace PristonTale.Protocol.Binary;

// Message codes of the original wire protocol.
public static class PristonOpcodes
{
    public const uint Connected = 0x48470080;
    public const uint RecordData = 0x48470081;
    public const uint GetRecordData = 0x48470082;
    public const uint FailRecordData = 0x48470083;
    public const uint RecordResult = 0x48470084;
    public const uint IdGetUserInfo = 0x48470085;
    public const uint IdSetUserInfo = 0x48470086;
    public const uint DelRecordData = 0x48470087;
    public const uint InsRecordData = 0x48470088;
    public const uint IsRecordData = 0x48470089;
    public const uint Version = 0x4847008A;
    public const uint CheckNetState = 0x4847008B;
    public const uint CheckNetState2 = 0x4847008E;
    public const uint CheckNetState3 = 0x4847008F;
    public const uint CheckNetState4 = 0x48470090;
    public const uint CheckNetState5 = 0x48470091;

    public const uint PlayerInfo = 0x48470020;
    public const uint FailConnect = 0x48470023;
    public const uint ServerInfo = 0x484700C0;

    public static bool IsHeartbeat(uint code) => code is CheckNetState or CheckNetState2 or CheckNetState3 or CheckNetState4 or CheckNetState5;
}

// Etervale-only messages for server-authoritative operations the original client performs locally.
// The high word "ET" (0x4554) is unused by the original protocol, so the original client ignores these codes.
public static class EtervaleOpcodes
{
    public const uint OwnerStatus = 0x45540100;
    public const uint Skills = 0x45540101;
    public const uint Inventory = 0x45540102;
    public const uint InventoryCommand = 0x45540110;
    public const uint InventoryResult = 0x45540111;
    public const uint ProgressionCommand = 0x45540120;
    public const uint ProgressionResult = 0x45540121;
    public const uint NpcResult = 0x45540130;
    public const uint NpcServiceRequest = 0x45540131;
    public const uint NpcServiceResult = 0x45540132;
    public const uint ShopCatalog = 0x45540133;
    public const uint ShopQuotes = 0x45540134;
    public const uint Warehouse = 0x45540135;
    public const uint TeleportList = 0x45540136;
    public const uint CombatEvents = 0x45540140;
    public const uint RemoveObjects = 0x45540150;
    public const uint LeaveWorld = 0x45540160;
    public const uint PresenceClock = 0x45540170;

    public const uint SkillCast = 0x45540200;
    public const uint SkillCastResult = 0x45540201;
    public const uint SkillEvents = 0x45540202;
    public const uint StatusEffects = 0x45540203;
    public const uint SkillCooldowns = 0x45540204;

    public const uint ItemDetails = 0x45540300;
    public const uint OwnerItemStatus = 0x45540301;
    public const uint ItemUseCommand = 0x45540400;
    public const uint ItemUseResult = 0x45540401;
    public const uint PremiumTimers = 0x45540402;
    public const uint ItemEffects = 0x45540403;
    public const uint QuestRequest = 0x45540500;
    public const uint QuestResult = 0x45540501;
    public const uint QuestJournal = 0x45540502;
    public const uint QuestNotices = 0x45540503;

    public const uint PresenceEntries = 0x45540900;
    public const uint DescribeEntity = 0x45540901;
    public const uint EntityDescription = 0x45540902;

    public const uint RestartRequest = 0x45540A00;
    public const uint RestartResult = 0x45540A01;
    public const uint RestartOffer = 0x45540A02;

    public const uint GroundItems = 0x45540B00;
    public const uint GroundItemsRemoved = 0x45540B01;

    public const uint ChatLine = 0x45540C00;
    public const uint ChatSay = 0x45540C01;

    public const uint MovementReport = 0x45540D00;
    public const uint AttackRequest = 0x45540D01;
    public const uint FieldWarp = 0x45540D02;
    public const uint CloseRequest = 0x45540D03;
}
