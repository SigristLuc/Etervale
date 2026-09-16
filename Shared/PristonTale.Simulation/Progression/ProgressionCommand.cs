using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Progression;

public enum ProgressionAction
{
    AllocateAttribute,
    LearnSkill
}

public sealed record ProgressionCommand(Guid OperationId, long ExpectedRevision, ProgressionAction Action,
    CharacterAttribute Attribute = CharacterAttribute.Strength, int Amount = 0, uint SkillCode = 0, uint NpcEntityId = 0);
public sealed record ProgressionOutcome(bool Ok, long Revision, string Message);
public sealed record AppliedProgressionOperation(ProgressionCommand Command, long Revision);

public static class SkillLearning
{
    private static readonly int[] BasePrices = [500, 1000, 1500, 2000];
    private static readonly int[] RankPrices = [500, 600, 700, 800];

    public static int Price(int index, int currentRank)
    {
        if (index is < 0 or > 3 || currentRank is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return BasePrices[index] + RankPrices[index] * currentRank;
    }

    public static bool TryQuote(PlayerCharacter character, uint code, out int price)
    {
        price = 0;
        var skills = FirstTierSkills.For(character.Definition.Class);
        for (var index = 0; index < skills.Count; index++)
        {
            var skill = skills[index];
            if (skill.Code != code)
            {
                continue;
            }

            var rank = character.Skills.RankOf(code);
            if (rank >= 10 || character.SkillPoints == 0 || character.Level < skill.RequiredLevel + rank * 2
                || index > 0 && character.Skills.RankOf(skills[index - 1].Code) == 0)
            {
                return false;
            }

            price = Price(index, rank);
            return character.Money >= price;
        }

        return false;
    }
}
