using System.Numerics;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Tests;

internal static class ProgressionAndOwnershipTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("original XP thresholds, multi-level rewards and job gate", ProgressionAsync);
        yield return ("inventory footprints, split identities and rejected swaps are atomic", GridAsync);
        yield return ("equipment validates requirements, handedness and original weapon formula", EquipmentAsync);
        yield return ("owned state round-trips detached copies and rejects unknown formats", PersistenceAsync);
    }

    private static Task ProgressionAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Progress");
        var attributes = character.Attributes;
        Assert.True(ExperienceTable.ForLevel(2) == 1000 && ExperienceTable.ForLevel(3) == 2500, "original XP thresholds changed");
        Assert.True(character.AwardExperience(2500) == 2 && character.Level == 3, "multi-level award did not resolve all thresholds");
        Assert.True(character.StatPoints == 10 && character.Attributes == attributes, "level-up silently redistributed attributes");
        Assert.True(character.TryAllocateAttribute(CharacterAttribute.Strength, 5), "available points rejected");
        Assert.True(!character.TryAllocateAttribute(CharacterAttribute.Health, 6), "unavailable points spent");
        Assert.True(character.Attributes.Strength == attributes.Strength + 5 && character.StatPoints == 5, "attribute allocation changed wrong state");
        character.AwardExperience(ExperienceTable.ForLevel(12) - character.Experience);
        Assert.True(character.Level == 12 && character.SkillPoints == 2, "even-level skill points were lost in a multi-level award");
        Assert.True(character.Life == character.MaxLife && character.Mana == character.MaxMana && character.Stamina == character.MaxStamina, "level-up pools not restored");
        character.RestoreProgress(ExperienceTable.ForLevel(100), 0);
        character.AwardExperience(1);
        Assert.True(character.Experience == ExperienceTable.ForLevel(100), "job quest XP gate bypassed");
        character.RestoreProgress(ulong.MaxValue, 0, tier: 3);
        character.AwardExperience(ulong.MaxValue);
        Assert.True(character.Experience == ulong.MaxValue, "overflow or loss of saved XP above cap");
        character.RestoreProgress(ExperienceTable.ForLevel(110), 0, tier: 3);
        character.AwardExperience(ulong.MaxValue);
        Assert.True(character.Experience == ExperienceTable.Ceiling && character.Level == ExperienceTable.MaximumLevel && ExperienceTable.MaximumLevel == 285,
            "a 64-bit award wrapped or the 285 levels are not reachable");
        Assert.True(ExperienceTable.ForLevel(121) == 1_632_547_885_967UL && ExperienceTable.ForLevel(285) == long.MaxValue,
            "the level table past 120 differs");
        var lost = character.LoseExperience(ulong.MaxValue);
        Assert.True(character.Experience == ExperienceTable.ForLevel(285) && lost == ExperienceTable.Ceiling - ExperienceTable.ForLevel(285),
            "a loss went below the level's start or wrapped");
        Assert.True(KillReward.ExpForKill(ulong.MaxValue, 1, 1, 5) == ulong.MaxValue
            && PremiumRules.BoostExperience(ulong.MaxValue / 2, new PremiumTimers()) == ulong.MaxValue / 2, "kill experience wrapped");
        Assert.True(!character.TryChangeMoney(-1) && character.TryChangeMoney(10) && !character.TryChangeMoney(long.MaxValue), "money overflow/overdraft accepted");
        return Task.CompletedTask;
    }

    private static Task GridAsync()
    {
        var inventory = new Inventory();
        var large = new ItemInstance(new ItemDefinition(1, "Armor", ItemKind.Armor, GridWidth: 2, GridHeight: 3));
        Assert.True(!inventory.TryPlace(large, 11), "footprint wrapped across a row");
        Assert.True(inventory.TryPlace(large, 0), "valid footprint rejected");
        var small = new ItemInstance(new ItemDefinition(2, "Stone", ItemKind.Material));
        Assert.True(!inventory.TryPlace(small, 13), "item overlapped covered non-anchor cell");
        Assert.True(inventory.TryMove(0, 1), "self-overlapping move rejected");
        Assert.True(inventory.TryPlace(small, 0), "move did not release old footprint");
        var potion = new ItemInstance(new ItemDefinition(3, "Potion", ItemKind.Potion, MaxStack: 10), 10);
        Assert.True(inventory.TryPlace(potion, 8) && inventory.TrySplit(8, 9, 4), "valid split failed");
        Assert.True(inventory[8].InstanceId != inventory[9].InstanceId && inventory[8].Count + inventory[9].Count == 10, "split duplicated identity or quantity");
        Assert.True(inventory.TryMerge(9, 8) && inventory[8].Count == 10 && inventory[9] is null, "merge did not preserve total");
        var before = inventory.Version;
        var oversized = new ItemInstance(new ItemDefinition(4, "Oversized", ItemKind.Misc, GridWidth: 13));
        Assert.True(!inventory.TryExchange(0, oversized) && inventory[0].InstanceId == small.InstanceId && inventory.Version == before, "failed exchange removed item");
        return Task.CompletedTask;
    }

    private static Task EquipmentAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Equipment", 6);
        var axe = new ItemInstance(new ItemDefinition(1, "Axe", ItemKind.Axe, EquipmentSlot.Weapon, TwoHanded: true)
        {
            BaseStats = new ItemStatBlock(10, 20, 0, 3, 0, 0, 0, 0, 0, 0)
        });
        axe.Roll = axe.Roll with { SpecJob = (uint)JobBitCode.Archer };
        var shield = new ItemInstance(new ItemDefinition(2, "Shield", ItemKind.Shield, EquipmentSlot.OffHand));
        Assert.True(character.Inventory.TryPlace(axe, 0) && character.TryEquipFromInventory(0, EquipmentSlot.Weapon), "weapon equip failed");
        Assert.True(!character.CanEquip(EquipmentSlot.OffHand, shield), "two-handed weapon allowed a shield");
        Assert.True(!character.CanEquip(EquipmentSlot.Armor, axe), "weapon accepted in armor slot");
        Assert.True(character.EquipmentTotals.AttackRating == 3, "off-class specialization removed base stats");
        var attributes = character.Attributes;
        var expectedMin = 2 + 10 * (attributes.Strength + 130) / 130 + (attributes.Talent + attributes.Agility) / 40 + 1 + 30 / 16;
        var expectedMax = 4 + 20 * (attributes.Strength + 130) / 130 + (attributes.Talent + attributes.Agility) / 40 + 1;
        var attack = CombatProfileFactory.ForPlayerBasicAttack(character);
        Assert.True(attack.DamageMin == expectedMin && attack.DamageMax == expectedMax, "weapon formula double-counted bare-hand terms");
        Assert.True(character.TryUnequip(EquipmentSlot.Weapon) && character.Inventory.Find(axe.InstanceId) >= 0, "unequip lost identity");
        return Task.CompletedTask;
    }

    private static async Task PersistenceAsync()
    {
        var classes = new CharacterClassRegistry();
        var record = CharacterRecordFactory.NewStarter(classes, 1, "Persist", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var character = classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        var definition = new ItemDefinition(1, "Potion", ItemKind.Potion, MaxStack: 10);
        var catalog = new ItemCatalog([definition]);
        character.AwardExperience(2500);
        character.TryChangeMoney(37);
        character.Inventory.TryPlace(new ItemInstance(definition, 4), 5);
        var captured = CharacterRecordFactory.Capture(record, character, 3, Vector3.Zero);
        var identity = character.Inventory[5].InstanceId;
        character.Inventory.ConsumeAt(5);
        var encoded = captured.Encode();
        var restored = new PlayerCharacter(classes.Get(PlayerClass.Fighter), record.Name, encoded.Level,
            new CharacterAttributes(encoded.Strength, encoded.Spirit, encoded.Talent, encoded.Agility, encoded.Health));
        CharacterStateCodec.Restore(restored, encoded, catalog);
        Assert.True(restored.Experience == 2500 && restored.Money == 37 && restored.StatPoints == 10, "saved progression lost");
        var veteran = encoded with { Exp = unchecked((long)ExperienceTable.Ceiling) };
        var reloaded = new PlayerCharacter(classes.Get(PlayerClass.Fighter), record.Name, ExperienceTable.MaximumLevel, restored.Attributes);
        CharacterStateCodec.Restore(reloaded, veteran, catalog);
        Assert.True(reloaded.Experience == ExperienceTable.Ceiling
            && CharacterRecordFactory.Capture(record, reloaded, 3, Vector3.Zero).Encode().Exp == veteran.Exp, "experience past 2^63 did not round-trip");
        Assert.True(restored.Inventory[5].Count == 4 && restored.Inventory[5].InstanceId == identity, "save snapshot aliases live inventory");
        // Weapon sets: the worn hands trade places with the spare set, which is carried, saved and never adds stats.
        var axe = new ItemDefinition(ItemCodes.Parse("WA101"), "Stone Axe", ItemKind.Axe, EquipmentSlot.Weapon) { CatalogKey = "WA101", Weight = 7 };
        var shield = new ItemDefinition(ItemCodes.Parse("DS101"), "Shield", ItemKind.Shield, EquipmentSlot.OffHand) { CatalogKey = "DS101", Weight = 5 };
        var hammer = new ItemDefinition(ItemCodes.Parse("WH101"), "Hammer", ItemKind.Hammer, EquipmentSlot.Weapon) { CatalogKey = "WH101", Weight = 9 };
        var armed = classes.CreatePlayer(PlayerClass.Fighter, "Armed");
        armed.Equip(EquipmentSlot.Weapon, new ItemInstance(axe));
        armed.Equip(EquipmentSlot.OffHand, new ItemInstance(shield));
        var weight = armed.CarriedWeight;
        Assert.True(armed.TrySwapWeaponSet() && armed.WeaponSet == 1 && armed.GetEquipped(EquipmentSlot.Weapon) is null
            && armed.SpareHands.Count == 2 && armed.CarriedWeight == weight, "swapping to an empty set did not keep the first set carried");
        armed.Equip(EquipmentSlot.Weapon, new ItemInstance(hammer));
        // Bag pages: page I's items leave the bag for page II and come back with the next swap.
        Assert.True(armed.Inventory.TryPlace(new ItemInstance(definition, 3), 0) && armed.TrySwapBagPage() && armed.BagPage == 1
            && armed.Inventory[0] is null && armed.SpareBagItems().Single().Item.Count == 3, "swapping bag pages did not set page I aside");
        Assert.True(armed.Inventory.TryPlace(new ItemInstance(definition, 6), 2), "page II refused an item");
        var saved = CharacterRecordFactory.Capture(record, armed, 3, Vector3.Zero).Encode();
        var rearmed = new PlayerCharacter(classes.Get(PlayerClass.Fighter), record.Name, saved.Level,
            new CharacterAttributes(saved.Strength, saved.Spirit, saved.Talent, saved.Agility, saved.Health));
        CharacterStateCodec.Restore(rearmed, saved, new ItemCatalog([definition, axe, shield, hammer]));
        Assert.True(rearmed.WeaponSet == 1 && rearmed.GetEquipped(EquipmentSlot.Weapon)?.Definition.CatalogKey == "WH101"
            && rearmed.SpareHands.Count == 2, "the spare weapon set was not saved");
        Assert.True(rearmed.TrySwapWeaponSet() && rearmed.WeaponSet == 0 && rearmed.GetEquipped(EquipmentSlot.Weapon)?.Definition.CatalogKey == "WA101"
            && rearmed.GetEquipped(EquipmentSlot.OffHand)?.Definition.CatalogKey == "DS101" && rearmed.SpareHands[EquipmentSlot.Weapon].Definition.CatalogKey == "WH101",
            "swapping back did not restore the first set");
        var heldAxe = rearmed.GetEquipped(EquipmentSlot.Weapon)!.InstanceId;
        Assert.True(rearmed.TrySwapWeaponSet(heldAxe) && rearmed.WeaponSet == 1 && rearmed.GetEquipped(EquipmentSlot.Weapon)?.InstanceId == heldAxe
            && rearmed.GetEquipped(EquipmentSlot.OffHand) is null && rearmed.SpareHands[EquipmentSlot.OffHand].Definition.CatalogKey == "DS101"
            && rearmed.SpareHands[EquipmentSlot.Weapon].Definition.CatalogKey == "WH101", "a held weapon did not go along to the other set");
        Assert.True(rearmed.BagPage == 1 && rearmed.Inventory[2]?.Count == 6 && rearmed.TrySwapBagPage() && rearmed.BagPage == 0
            && rearmed.Inventory[0]?.Count == 3 && rearmed.SpareBagItems().Single().Anchor == 2, "the other bag page was not saved");
        var carried = rearmed.Inventory[0]!.InstanceId;
        Assert.True(rearmed.TrySwapBagPage(carried) && rearmed.BagPage == 1 && rearmed.Inventory.Find(carried) >= 0
            && !rearmed.SpareBagItems().Any(), "an item held on the cursor did not travel to the other page");
        var twoHanded = new ItemDefinition(ItemCodes.Parse("WP121"), "Scythe", ItemKind.Pole, EquipmentSlot.Weapon) { CatalogKey = "WP121", TwoHanded = true };
        var reaper = classes.CreatePlayer(PlayerClass.Pikeman, "Reaper");
        reaper.Equip(EquipmentSlot.Weapon, new ItemInstance(twoHanded));
        reaper.TrySwapWeaponSet();
        var buckler = new ItemInstance(shield);
        reaper.Equip(EquipmentSlot.OffHand, buckler);
        var heldShield = new InventoryCommand(Guid.NewGuid(), reaper.InventoryRevision, InventoryAction.SwapWeaponSet, buckler.InstanceId, 0, 0, default);
        Assert.True(InventoryCommands.Apply(reaper, heldShield) && reaper.WeaponSet == 0 && reaper.GetEquipped(EquipmentSlot.Weapon)?.Definition == twoHanded
            && reaper.GetEquipped(EquipmentSlot.OffHand) is null && reaper.Inventory.Find(buckler.InstanceId) >= 0 && !reaper.SpareHands.Any(),
            "a shield held into a two-handed weapon's set did not leave the hands for the bag");
        Assert.True(reaper.TryEquipFromInventory(reaper.Inventory.Find(buckler.InstanceId), EquipmentSlot.OffHand)
            && reaper.GetEquipped(EquipmentSlot.OffHand)?.InstanceId == buckler.InstanceId && reaper.GetEquipped(EquipmentSlot.Weapon) is null
            && reaper.Inventory.Find(reaper.OwnedItems().First(item => item.Definition == twoHanded).InstanceId) >= 0,
            "a shield put on the two-handed weapon did not trade places with it");
        Assert.True(rearmed.TryUnequip(EquipmentSlot.Weapon, 40) && rearmed.Inventory[40]?.InstanceId == heldAxe,
            "an unequipped item did not land in the bag cell asked for");
        var corrupt = encoded with { Inventory = new JsonText("{\"Version\":2,\"Items\":[]}") };
        var repository = new MemoryCharacters(corrupt);
        var registry = new SessionRegistry();
        var session = registry.Create(1, "owner");
        var service = new SessionAppService(registry, classes, repository, new WorldCatalog());
        Assert.True(!(await service.EnterWorldAsync(session.SessionToken, record.Name)).Ok, "future version was treated as empty inventory");
        Assert.True(!await service.SaveCharacterAsync(session.SessionToken) && repository.Record.Inventory == corrupt.Inventory, "failed hydration overwrote saved state");
    }
}
