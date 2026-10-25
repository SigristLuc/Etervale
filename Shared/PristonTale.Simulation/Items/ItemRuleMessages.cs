namespace PristonTale.Simulation.Items;

// The texts a player sees when an item rule refuses an action, as the original client words them.
public static class ItemRuleMessages
{
    public const string OverWeight = "You have exceeded the inventory weight limit";
    public const string CannotUse = "Item cannot be used yet";
    public const string NoRoomInBag = "There is no room for this item in the bag";
    public const string NoRoomOnOtherPage = "There is no room for this item on the other bag page";
}
