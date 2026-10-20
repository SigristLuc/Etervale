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

    public const uint PlayData1 = 0x48470010;
    public const uint TransPlayData = 0x48470013;
    public const uint PlayDataGroup = 0x48470014;
    public const uint PlayerInfo = 0x48470020;
    public const uint GetPlayerInfo = 0x48470021;
    public const uint FailConnect = 0x48470023;
    public const uint AddExp = 0x48470031;
    public const uint PlayerInfo2 = 0x48470032;
    public const uint WarpField = 0x48470040;
    public const uint PlayItem = 0x48470050;
    public const uint DelItem = 0x48470051;
    public const uint PutItem = 0x48470052;
    public const uint ServerInfo = 0x484700C0;
    public const uint CloseClient = 0x484700E6;
    public const uint SaveClient = 0x484700E8;
    public const uint ChatMessage = 0x48471001;
    public const uint WhisperMessage = 0x48471005;

    public const uint AttackDamage = 0x50322000;
    public const uint AttackResult = 0x50322020;

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
    public const uint CombatEvents = 0x45540140;
    public const uint RemoveObjects = 0x45540150;
    public const uint LeaveWorld = 0x45540160;
    public const uint PresenceClock = 0x45540170;
}
