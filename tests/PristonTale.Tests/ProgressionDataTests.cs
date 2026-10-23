using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;

namespace PristonTale.Tests;

internal static class ProgressionDataTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("later-tier class skills take the original codes and save their rank and use counter", AdvancedSkillsAsync);
        yield return ("item footprints follow the item table and a saved item whose footprint grew moves to a free place", FootprintsAsync);
    }

    private static Task AdvancedSkillsAsync()
    {
        var classes = new CharacterClassRegistry();
        var mechanician = classes.Get(PlayerClass.Mechanician).Skills;
        Assert.True(mechanician.Single(skill => skill.Name == "Great Smash").Code == 0x01020001
            && mechanician.Single(skill => skill.Name == "Mechanic Weapon Mastery").Code == 0x01040003
            && mechanician.Single(skill => skill.Name == "Metal Golem").Code == 0x01080004
            && classes.Get(PlayerClass.Fighter).Skills.Single(skill => skill.Name == "Boost Health").Code == 0x02080004,
            "later-tier skills do not take the class group, tier bit and slot");
        Assert.True(mechanician.Count == 16 && mechanician.Select(skill => skill.Code).Distinct().Count() == 16, "class skill codes collide");

        var player = classes.CreatePlayer(PlayerClass.Mechanician, "Trained", 90);
        foreach (var skill in mechanician)
        {
            player.Skills.Restore(skill.Code, 10, LearnedSkills.MaxUses);
        }

        var starter = CharacterRecordFactory.NewStarter(classes, 1, player.Name, 0, PlayerClass.Mechanician, 3);
        var record = CharacterRecordFactory.FromLiveSession(starter, player, 3, System.Numerics.Vector3.Zero);
        var restored = classes.CreatePlayer(PlayerClass.Mechanician, "Trained", 90);
        CharacterStateCodec.Restore(restored, record, new ItemCatalog([]));
        Assert.True(mechanician.All(skill => restored.Skills.RankOf(skill.Code) == 10 && restored.Skills.UsesOf(skill.Code) == LearnedSkills.MaxUses),
            "ranks or use counters were not saved");

        var rejected = false;
        try
        {
            classes.CreatePlayer(PlayerClass.Assassin, "Uncoded").Skills.Restore(0, 1);
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        Assert.True(rejected, "a skill without a code was learned");
        return Task.CompletedTask;
    }

    private static Task FootprintsAsync()
    {
        Assert.True(ItemGridSizes.Of("WA102") == (2, 3) && ItemGridSizes.Of("da102") == (3, 4) && ItemGridSizes.Of("PL101") == (1, 1)
            && ItemGridSizes.Of("ZZ999") == (1, 1), "item footprints do not follow the item table");

        var small = new ItemDefinition(0x0101_0001, "Axe", ItemKind.Axe, EquipmentSlot.Weapon) { CatalogKey = "WA102" };
        var other = new ItemDefinition(0x0101_0002, "Potion", ItemKind.Potion) { CatalogKey = "PL101" };
        var classes = new CharacterClassRegistry();
        var player = classes.CreatePlayer(PlayerClass.Fighter, "Packer", 10);
        var axe = new ItemInstance(small);
        var potion = new ItemInstance(other);
        Assert.True(player.Inventory.TryPlace(axe, 0) && player.Inventory.TryPlace(potion, 1), "fixture items were not placed");
        var starter = CharacterRecordFactory.NewStarter(classes, 1, player.Name, 0, PlayerClass.Fighter, 3);
        var record = CharacterRecordFactory.FromLiveSession(starter, player, 3, System.Numerics.Vector3.Zero);

        var grown = small with { GridWidth = 2, GridHeight = 3 };
        var restored = classes.CreatePlayer(PlayerClass.Fighter, "Packer", 10);
        CharacterStateCodec.Restore(restored, record, new ItemCatalog([grown, other]));
        var axeAnchor = restored.Inventory.Find(axe.InstanceId);
        var potionAnchor = restored.Inventory.Find(potion.InstanceId);
        Assert.True(axeAnchor == 0 && potionAnchor >= 0 && potionAnchor != 1, "the item under the grown footprint did not move");
        return Task.CompletedTask;
    }
}
