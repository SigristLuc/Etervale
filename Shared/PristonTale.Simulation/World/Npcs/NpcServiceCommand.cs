using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.World.Npcs;

// Persisted in the character's operation history and carried on the wire; append values only.
public enum NpcServiceAction : byte
{
    Open = 0,
    Close = 1,
    Refresh = 2,
    Buy = 10,
    Sell = 11,
    Repair = 12,
    RepairAll = 13,
    Deposit = 20,
    Withdraw = 21,
    Move = 22,
    DepositGold = 23,
    WithdrawGold = 24,
    Teleport = 30
}

public enum NpcServiceStatus : ushort
{
    Ok = 0,
    NotAvailable = 1,
    OutOfReach = 2,
    Rejected = 3,
    Stale = 4,
    NotEnoughGold = 5,
    NoSpace = 6,
    GoldLimit = 7,
    Busy = 8,
    StorageFailed = 9,
    Conflict = 10,
    NotOpen = 11,
    OverWeight = 12
}

// ItemCode names a stock entry to buy; ItemId an owned item. Anchor is a destination cell (-1 = first free) or, for a
// teleport, the destination index. Amount is gold for the gold actions.
public sealed record NpcServiceCommand(
    Guid OperationId,
    uint NpcEntityId,
    NpcServiceKind Service,
    NpcServiceAction Action,
    long ExpectedRevision = 0,
    long ExpectedStorageRevision = 0,
    uint ItemCode = 0,
    Guid ItemId = default,
    int Anchor = -1,
    int Count = 1,
    long Amount = 0);

public static class NpcServiceActions
{
    // Durable actions change the character or its storage and are written before they take effect.
    public static bool IsDurable(NpcServiceAction action) => action is not (NpcServiceAction.Open or NpcServiceAction.Close or NpcServiceAction.Refresh);
}

public sealed record AppliedServiceOperation(NpcServiceCommand Command, long Revision);

public sealed record NpcServiceOutcome(bool Ok, NpcServiceStatus Status, long Revision, long StorageRevision, string Message)
{
    public static NpcServiceOutcome Failed(NpcServiceStatus status, string message, long revision = 0, long storageRevision = 0) =>
        new(false, status, revision, storageRevision, message);
}

public sealed record ShopOffer(uint Code, int Tab, int Ordinal, string Name, ItemKind Kind, long Price, int Width, int Height, int MaxStack,
    string CatalogKey = "");

public sealed record ShopQuote(Guid ItemId, long SellPrice, long RepairCost, int Durability, int MaxDurability);

public sealed record ShopQuotes(long InventoryRevision, IReadOnlyList<ShopQuote> Items);

public sealed record WarehouseView(long Revision, long Money, int Weight, int MaxWeight, int Columns, int Rows, IReadOnlyList<OwnedItemView> Items);

public sealed record TeleportOffer(int Index, int FieldCode, string Name, long Price, int MinimumLevel);

// The reply to one service request: the outcome plus the state the service shows after it.
public sealed record NpcServiceReply(NpcServiceOutcome Outcome)
{
    public IReadOnlyList<ShopOffer> Catalog { get; init; }
    public ShopQuotes Quotes { get; init; }
    public WarehouseView Warehouse { get; init; }
    public IReadOnlyList<TeleportOffer> Destinations { get; init; }
    public TeleportArrival Arrival { get; init; }
}

public sealed record TeleportArrival(int FieldCode, System.Numerics.Vector3 Position);
