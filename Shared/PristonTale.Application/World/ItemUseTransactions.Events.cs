using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Application.World;

// Event items: foods and gift boxes are eaten like potions (one at a time, never while dead or eating); a gift box needs
// room for a 2 x 2 item and spare weight, then its prize lands in the inventory. Chocolates grant Virtual Life to the
// gender they are meant for; a puzzle piece sealed as a lucky bag is revealed for 1000 gold.
internal static partial class ItemUseTransactions
{
    private static ItemUseStatus CheckFood(ItemUseContext context, ItemInstance item)
    {
        if (item.Definition.Potion is not { IsValid: true })
        {
            return ItemUseStatus.NotUsable;
        }

        return context.Player.HasPendingPotion ? ItemUseStatus.Delay : ItemUseStatus.Used;
    }

    private static ItemUseStatus CheckGiftBox(ItemUseContext context, UsableItemRule rule)
    {
        var character = context.Player.Character;
        if (context.Player.HasPendingPotion)
        {
            return ItemUseStatus.Delay;
        }

        if (!character.Inventory.HasRoom(GiftBoxes.RoomWidth, GiftBoxes.RoomHeight))
        {
            return ItemUseStatus.NoSpace;
        }

        return character.CarriedWeight + GiftBoxes.WeightMargin(rule.Argument) >= character.Derived.MaxWeight ? ItemUseStatus.OverWeight : ItemUseStatus.Used;
    }

    // The prize joins the candidate after the box left it; an unknown prize item or a full inventory refuses the box.
    private static ItemUseExtra ExtendGiftBox(ItemUseContext context, UsableItemRule rule, PlayerCharacter candidate)
    {
        var prize = GiftBoxes.Draw(rule.Argument, context.Pick);
        if (!TryCreate(context.Items, prize.CatalogKey, prize.Count, out var item))
        {
            return new ItemUseExtra(ItemUseStatus.NotUsable);
        }

        if (!candidate.Inventory.TryAdd(item))
        {
            return new ItemUseExtra(ItemUseStatus.NoSpace);
        }

        var count = item.Count > 1 ? $" x{item.Count}" : "";
        return new ItemUseExtra(ItemUseStatus.Used, Food: GiftBoxMeal, Message: $"You received {item.Definition.Name}{count}");
    }

    private static ItemUseStatus CheckChocolate(ItemUseContext context, ItemInstance item)
    {
        if (!Chocolates.TryGet(item.Definition.CatalogKey, out var chocolate))
        {
            return ItemUseStatus.NotUsable;
        }

        return context.Player.Appearance.InVillage ? ItemUseStatus.NotHere : Chocolates.Check(chocolate, context.Player.Character);
    }

    private static ItemUseStatus CheckPuzzleBag(ItemUseContext context, ItemInstance item)
    {
        if (!item.Sealed)
        {
            return ItemUseStatus.NothingToChange;
        }

        return context.Player.Character.Money < Puzzles.RevealCost ? ItemUseStatus.NotEnoughGold : ItemUseStatus.Used;
    }

    private static ItemUseExtra ExtendPuzzleBag(ItemInstance item, PlayerCharacter candidate)
    {
        var anchor = candidate.Inventory.Find(item.InstanceId);
        if (anchor < 0 || !candidate.TryChangeMoney(-Puzzles.RevealCost))
        {
            return new ItemUseExtra(ItemUseStatus.NotUsable);
        }

        candidate.Inventory[anchor].Sealed = false;
        candidate.TouchInventory();
        return new ItemUseExtra(ItemUseStatus.Used, Money: -Puzzles.RevealCost, Message: $"The lucky bag held {item.Definition.Name}");
    }

    // An eat motion that restores nothing: gift boxes and capsules are eaten for show.
    private static readonly PotionDefinition GiftBoxMeal = new(0, 0, 0, 0, 0, 0);

    // A new copy of a catalog item in its shop form; stackable items come in the given count.
    internal static bool TryCreate(ItemCatalog items, string catalogKey, int count, out ItemInstance item)
    {
        item = null!;
        if (!ItemCodes.TryParse(catalogKey, out var code) || !items.TryGet(code, out var definition))
        {
            return false;
        }

        item = new ItemInstance(definition, Math.Clamp(count, 1, Math.Max(1, definition.MaxStack)));
        return true;
    }
}
