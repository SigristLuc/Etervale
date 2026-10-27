using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Characters;

public sealed record CharacterClassDef(
    PlayerClass Class,
    string Name,
    CharacterBrood Brood,
    CharacterVisualSet DefaultVisuals,
    CharacterAttributes BaseAttributes,
    CharacterAttributes GrowthPerLevel,
    int StatusPointsPerLevel,
    CharacterFunctionProfile FunctionProfile,
    IReadOnlyList<EquipmentRef> StartingEquipment)
{
    public uint MotionJobMask => 1u << ((int)Class - 1);

    public JobBitCode JobBitCode => JobBits.Of(Class);

    public CharacterAttributes AttributesAtLevel(int level)
    {
        var levelDelta = Math.Max(0, level - 1);
        return BaseAttributes.Add(GrowthPerLevel.Scale(levelDelta));
    }

    public int UnallocatedStatusPointsAtLevel(int level)
    {
        var levelDelta = Math.Max(0, level - 1);
        return levelDelta * StatusPointsPerLevel;
    }
}
