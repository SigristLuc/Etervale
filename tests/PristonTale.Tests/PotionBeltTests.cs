using System.Numerics;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

internal static class PotionBeltTests
{
    private static readonly ItemDefinition Life = Potion("PL101", "Mini Life Potion");
    private static readonly ItemDefinition Mana = Potion("PM101", "Mini Mana Potion");
    private static readonly ItemDefinition Bracelet = new(ItemCodes.Parse("OA209"), "Wing Bracelets", ItemKind.Bracelet, EquipmentSlot.Bracelet)
    {
        CatalogKey = "OA209",
        PotionSpace = 36
    };

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("potions stack to 999 in the bag whatever their authored stack size", StackLimitAsync);
        yield return ("a belt slot takes a potion stack up to the belt capacity, tops up the same potion and trades places with another", MoveAsync);
        yield return ("bought and picked-up potions fill the belt's slots of that potion, then the first empty slot, then the bag", CarryAsync);
        yield return ("belt potions are drunk and dropped, and a lower belt capacity sends the excess to the bag", ConsumeAndSettleAsync);
        yield return ("the belt is saved, cloned for operations and shown to the owner past the equipment slots", PersistAsync);
    }

    private static ItemDefinition Potion(string key, string name) => new(ItemCodes.Parse(key), name, ItemKind.Potion,
        MaxStack: ItemDefinition.StackLimit(ItemKind.Potion, 1))
    {
        CatalogKey = key,
        Potion = new PotionDefinition(60, 70, 0, 0, 0, 0)
    };

    private static PlayerCharacter Player(string name) => new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, name);

    private static Task StackLimitAsync()
    {
        Assert.True(ItemDefinition.StackLimit(ItemKind.Potion, 1) == 999 && ItemDefinition.StackLimit(ItemKind.Potion, 0) == 999
            && ItemDefinition.StackLimit(ItemKind.Misc, 1) == 1 && ItemDefinition.StackLimit(ItemKind.Material, 20) == 20,
            "only potions take the 999 stack limit");
        var character = Player("Stacker");
        Assert.True(character.Inventory.TryAdd(new ItemInstance(Life, 990)) && character.Inventory.TryAdd(new ItemInstance(Life, 20))
            && character.Inventory[0]?.Count == 999 && character.Inventory[1]?.Count == 11, "a bag stack did not fill to 999 before a new one");
        return Task.CompletedTask;
    }

    private static Task MoveAsync()
    {
        var character = Player("Belted");
        var life = new ItemInstance(Life, 5);
        Assert.True(character.Inventory.TryPlace(life, 0) && character.Derived.PotionSpace == 2, "default belt capacity is not 2");
        Assert.True(character.TryMoveToBelt(life.InstanceId, 0) && character.Belt[0]?.Count == 2 && character.Inventory[0]?.Count == 3,
            "an empty slot took more than the belt capacity or nothing");
        Assert.True(!character.TryMoveToBelt(life.InstanceId, 0), "a full slot of the same potion took more");
        character.Equip(EquipmentSlot.Bracelet, new ItemInstance(Bracelet));
        Assert.True(character.Derived.PotionSpace == 36 && character.TryMoveToBelt(life.InstanceId, 0) && character.Belt[0]?.Count == 5
            && character.Inventory[0] is null, "the bracelet's capacity did not let the slot take the whole stack");

        var mana = new ItemInstance(Mana, 4);
        character.Inventory.TryPlace(mana, 7);
        Assert.True(character.TryMoveToBelt(mana.InstanceId, 0) && character.Belt[0]?.InstanceId == mana.InstanceId
            && character.Inventory[7]?.Definition == Life && character.Inventory[7]?.Count == 5, "another potion did not trade places with the slot");
        Assert.True(character.TryMoveToBelt(mana.InstanceId, 2) && character.Belt[0] is null && character.Belt[2]?.InstanceId == mana.InstanceId,
            "a belt stack did not move to an empty slot");
        Assert.True(character.TryMoveFromBelt(mana.InstanceId, 20) && character.Inventory[20]?.InstanceId == mana.InstanceId && character.Belt[2] is null,
            "a belt stack did not go to the bag cell");
        var back = character.Inventory[7]!;
        character.TryMoveToBelt(back.InstanceId, 1);
        Assert.True(character.Inventory.TryPlace(new ItemInstance(Life, 3), 30) && character.TryMoveFromBelt(back.InstanceId, 30)
            && character.Inventory[30]?.Count == 8 && character.Belt[1] is null, "a belt stack did not merge into the bag stack under it");
        Assert.True(!character.TryMoveToBelt(new ItemInstance(Life).InstanceId, 0)
            && !character.TryMoveToBelt(character.Inventory[30]!.InstanceId, BeltSlotOutOfRange), "the belt took an unknown stack or slot");
        return Task.CompletedTask;
    }

    private const int BeltSlotOutOfRange = 3;

    private static Task CarryAsync()
    {
        var character = Player("Carrier");
        character.Belt.TryPlace(new ItemInstance(Mana, 1), 0);
        Assert.True(character.TryCarry(new ItemInstance(Life, 3)) && character.Belt[1]?.Definition == Life && character.Belt[1]?.Count == 2
            && character.Belt[0]?.Definition == Mana && character.Inventory[0]?.Count == 1, "a new potion did not take the first empty slot and spill to the bag");
        Assert.True(character.TryCarry(new ItemInstance(Mana, 2)) && character.Belt[0]?.Count == 2 && character.Belt[2]?.Definition == Mana
            && character.Belt[2]?.Count == 1, "the slot of the same potion was not topped up before an empty slot");
        Assert.True(character.TryCarry(new ItemInstance(Life, 2)) && character.Inventory[0]?.Count == 3, "a full belt did not send potions to the bag");

        var loot = Player("Looter");
        Assert.True(loot.ReceiveGroundLoot(new ItemInstance(Life, 4), 0) && loot.Belt[0]?.Count == 2 && loot.Belt[1] is null && loot.Inventory[0]?.Count == 2,
            "picked-up potions did not go to the belt");
        var shopper = Player("Shopper");
        shopper.TryChangeMoney(10_000);
        Assert.True(NpcShopRules.TryBuy(shopper, Mana, 3, -1, out _) == NpcServiceStatus.Ok && shopper.Belt[0]?.Count == 2 && shopper.Inventory[0]?.Count == 1,
            "bought potions did not go to the belt");

        var full = Player("Full");
        for (var anchor = 0; anchor < full.Inventory.Capacity; anchor++)
        {
            full.Inventory.TryPlace(new ItemInstance(new ItemDefinition(9, "Rock", ItemKind.Misc)), anchor);
        }

        var revision = full.InventoryRevision;
        Assert.True(!full.TryCarry(new ItemInstance(Life, 7)) && full.Belt[0] is null && full.InventoryRevision == revision,
            "potions the bag could not take changed the belt");
        return Task.CompletedTask;
    }

    private static Task ConsumeAndSettleAsync()
    {
        var character = Player("Drinker");
        var bracelet = new ItemInstance(Bracelet);
        character.Equip(EquipmentSlot.Bracelet, bracelet);
        var life = new ItemInstance(Life, 30);
        character.Belt.TryPlace(life, 0);
        Assert.True(InventoryCommands.Apply(character, Command(character, InventoryAction.ConsumePotion, life.InstanceId, 0, 1))
            && character.Belt[0]?.Count == 29, "a belt potion was not drunk");
        Assert.True(InventoryCommands.Apply(character, Command(character, InventoryAction.Unequip, bracelet.InstanceId, -1, 0, EquipmentSlot.Bracelet))
            && character.Derived.PotionSpace == 2 && character.Belt[0]?.Count == 2 && character.Inventory.Find(life.InstanceId) < 0
            && character.OwnedItems().Where(item => item.Definition == Life).Sum(item => item.Count) == 29,
            "taking the bracelet off did not move the excess to the bag");
        var kept = character.Belt[0]!.InstanceId;
        Assert.True(InventoryCommands.Apply(character, Command(character, InventoryAction.Drop, kept, 0, 2)) && character.Belt[0] is null,
            "a belt stack was not dropped");
        var stale = Command(character, InventoryAction.ToBelt, character.OwnedItems().First(item => item.Definition == Life).InstanceId, 1, 0);
        Assert.True(InventoryCommands.Apply(character, stale) && character.Belt[1]?.Count == 2, "the ToBelt command did not fill the slot");
        Assert.True(InventoryCommands.Refusal(character, Command(character, InventoryAction.ToBelt, character.Inventory[character.Inventory.Find(
            character.OwnedItems().First(item => item.Definition == Life && character.Belt.Find(item.InstanceId) < 0).InstanceId)]!.InstanceId, 1, 0))
            == ItemRuleMessages.BeltFull, "a full slot was not named as the refusal");
        return Task.CompletedTask;
    }

    private static Task PersistAsync()
    {
        var classes = new CharacterClassRegistry();
        var record = CharacterRecordFactory.NewStarter(classes, 1, "Saver", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var character = classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        character.Belt.TryPlace(new ItemInstance(Life, 2), 0);
        character.Belt.TryPlace(new ItemInstance(Mana, 1), 2);
        var saved = CharacterRecordFactory.Capture(record, character, 3, Vector3.Zero).Encode();
        var restored = classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        CharacterStateCodec.Restore(restored, saved, new ItemCatalog([Life, Mana]));
        Assert.True(restored.Belt[0]?.Count == 2 && restored.Belt[0]?.InstanceId == character.Belt[0]!.InstanceId && restored.Belt[1] is null
            && restored.Belt[2]?.Definition == Mana, "the belt did not round-trip through the inventory column");
        Assert.True(restored.CarriedWeight == 3, "belt potions are not weighed");

        var candidate = restored.CloneForInventoryOperation();
        Assert.True(InventoryCommands.Apply(candidate, Command(candidate, InventoryAction.ConsumePotion, candidate.Belt[0]!.InstanceId, 0, 1))
            && restored.Belt[0]?.Count == 2, "the operation candidate shared the live belt");
        restored.CommitInventoryOperation(candidate);
        var revision = restored.InventoryRevision;
        Assert.True(restored.Belt[0]?.Count == 1 && restored.TryConsumePotion(restored.Belt[0]!.InstanceId) && restored.InventoryRevision > revision,
            "the committed belt did not adopt the candidate or raise the inventory revision");

        var owner = OwnerState.Capture(restored);
        var shown = owner.Items.Single(item => item.Code == Mana.Code);
        Assert.True(shown.Slot == OwnedItemView.BeltSlotBase + 2 && shown.BeltSlot == 2 && !shown.Worn && !shown.InBag,
            "the owner state did not show the belt stack past the equipment slots");

        var rock = new ItemDefinition(9, "Rock", ItemKind.Misc);
        var packed = classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        for (var anchor = 0; anchor < packed.Inventory.Capacity; anchor++)
        {
            packed.Inventory.TryPlace(new ItemInstance(rock), anchor);
        }

        packed.Belt.TryPlace(new ItemInstance(Life, 2), 1);
        var full = CharacterRecordFactory.Capture(record, packed, 3, Vector3.Zero).Encode();
        var reloaded = classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        CharacterStateCodec.Restore(reloaded, full, new ItemCatalog([Life, rock]));
        Assert.True(full.Inventory.Value.Length > 65536 && Enumerable.Range(0, reloaded.Inventory.Capacity).All(anchor => reloaded.Inventory[anchor] is not null) && reloaded.Belt[1]?.Count == 2,
            "a full bag saved past 64 KB did not load back");
        return Task.CompletedTask;
    }

    private static InventoryCommand Command(PlayerCharacter character, InventoryAction action, Guid item, int destination, int count,
        EquipmentSlot slot = default) =>
        new(Guid.NewGuid(), character.InventoryRevision, action, item, destination, count, slot);
}
