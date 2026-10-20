using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;
using System.Text.Json;

namespace PristonTale.Tests;

internal static class FirstTierSkillTests
{
    private sealed record SourceSkill(uint Code, string Name, string Description, string Icon, int Level, int Stamina, int Step, string Use, int[] Mana);
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("first-tier metadata preserves original IDs, levels, mouse sides and ten-rank costs", MetadataAsync);
    }

    private static Task MetadataAsync()
    {
        var seen = new HashSet<uint>();
        var classes = new CharacterClassRegistry();
        foreach (var playerClass in Enum.GetValues<PlayerClass>())
        {
            var skills = FirstTierSkills.For(playerClass);
            Assert.True(skills.Count == 4, "class must expose four first-tier skills");
            var character = classes.CreatePlayer(playerClass, "Metadata");
            var levels = new[] { 10, 12, 14, 17 };
            for (var index = 0; index < skills.Count; index++)
            {
                var skill = skills[index];
                Assert.True(seen.Add(skill.Code), "duplicate original skill identity");
                Assert.True(ReferenceEquals(character.Class.Skills[index], skill), "class retained a parallel first-tier definition");
                Assert.True(skill.RequiredLevel == levels[index] && skill.ManaByRank.Count == 10, "level or rank count changed");
                Assert.True(skill.IconKey.Length > 0 && skill.Description.Length > 0, "original presentation metadata missing");
                for (var rank = 1; rank <= 10; rank++)
                {
                    Assert.True(skill.ManaForRank(rank) >= 0 && skill.StaminaForRank(rank) >= 0, "negative resource cost");
                    if (skill.Category == SkillCategory.Passive)
                    {
                        Assert.True(skill.MouseUse == SkillMouseUse.None && skill.ManaForRank(rank) == 0 && skill.StaminaForRank(rank) == 0,
                            "passive skill is assignable or consumes cast resources");
                    }
                }
            }
        }

        Assert.True(seen.Count == 40, "first-tier catalog is incomplete");
        VerifySourceSnapshot();
        var mechanic = FirstTierSkills.For(PlayerClass.Mechanician);
        Assert.True(mechanic[2].Code == 0x01010004 && mechanic[3].Code == 0x01010003, "display order was incorrectly used as skill identity");
        Assert.True(mechanic[0].ManaForRank(1) == 12 && mechanic[0].ManaForRank(10) == 44 && mechanic[0].StaminaForRank(10) == 52,
            "Extreme Shield costs disagree with original arrays");
        Assert.True(mechanic[0].AllowsWeapon(ItemKind.Shield) && !mechanic[0].AllowsWeapon(ItemKind.Axe), "shield restriction lost");
        Assert.True(FirstTierSkills.For(PlayerClass.Knight)[3].MouseUse == SkillMouseUse.Right, "Double Crash incorrectly allowed on left mouse");
        Assert.True(FirstTierSkills.For(PlayerClass.Priestess)[2].MouseUse == SkillMouseUse.Right, "Multi Spark incorrectly allowed on left mouse");
        Assert.True(FirstTierSkills.For(PlayerClass.Magician)[0].ManaForRank(10) == 14, "Agony rank 10 mana cost changed");
        Assert.True(FirstTierSkills.For(PlayerClass.Assassin)[2].AllowsWeapon(ItemKind.Dagger), "Dual Wield Mastery lost its dagger restriction");
        Assert.True(FirstTierSkills.For(PlayerClass.Shaman)[0].AllowsWeapon(ItemKind.Phantom), "Dark Bolt lost its phantom restriction");
        return Task.CompletedTask;
    }

    private static void VerifySourceSnapshot()
    {
        using var stream = typeof(FirstTierSkillTests).Assembly.GetManifestResourceStream("PristonTale.Tests.Fixtures.first-tier-source.json")
            ?? throw new InvalidOperationException("Original skill fixture is missing.");
        var rows = JsonSerializer.Deserialize<SourceSkill[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var actual = Enum.GetValues<PlayerClass>().SelectMany(FirstTierSkills.For).ToDictionary(skill => skill.Code);
        foreach (var source in rows)
        {
            var skill = actual[source.Code];
            Assert.True(skill.Name == source.Name && skill.Description == source.Description && skill.IconKey == source.Icon, "source identity/presentation changed");
            Assert.True(skill.RequiredLevel == source.Level, "source required level changed");
            var mouseUse = source.Use switch
            {
                "Both" => SkillMouseUse.Both,
                "Right" => SkillMouseUse.Right,
                "Left" => SkillMouseUse.Left,
                "None" => SkillMouseUse.None,
                _ => throw new InvalidDataException(source.Use)
            };
            Assert.True(skill.MouseUse == mouseUse, "source mouse use changed");
            for (var rank = 1; rank <= 10; rank++)
            {
                Assert.True(skill.ManaForRank(rank) == source.Mana[rank - 1], "source mana cost changed");
                Assert.True(skill.StaminaForRank(rank) == source.Stamina + source.Step * (rank - 1), "source stamina cost changed");
            }
        }
    }
}
