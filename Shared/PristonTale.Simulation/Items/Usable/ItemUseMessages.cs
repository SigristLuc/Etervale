namespace PristonTale.Simulation.Items.Usable;

// The line the player reads for each use result.
public static class ItemUseMessages
{
    public static string For(ItemUseStatus status) => status switch
    {
        ItemUseStatus.Used => "Item used",
        ItemUseStatus.NotUsable => "This item cannot be used",
        ItemUseStatus.Stale => "The inventory changed; try again",
        ItemUseStatus.Busy => "Another action is pending; try again",
        ItemUseStatus.AlreadyRunning => "This item is already in use",
        ItemUseStatus.SameAttribute => "An item with the same attribute is already in use",
        ItemUseStatus.NotHere => "This item cannot be used here",
        ItemUseStatus.LevelTooLow => "Your level is too low",
        ItemUseStatus.NothingToChange => "There is nothing for this item to change",
        ItemUseStatus.Dead => "Cannot use items while dead",
        ItemUseStatus.Delay => "Wait a moment before using another one",
        ItemUseStatus.Conflict => "Operation identity conflict",
        ItemUseStatus.StorageFailed => "Storage rejected the operation; character unchanged",
        _ => "The item could not be used"
    };
}
