namespace PristonTale.Simulation.Characters;

public sealed record CharacterClassDef(
    PlayerClass Class,
    string Name,
    int JobCode,
    JobBitCode JobBitCode,
    CharacterBrood Brood,
    CharacterVisualSet DefaultVisuals,
    CharacterMotionName DefaultMotion,
    CharacterAttributes BaseAttributes,
    CharacterAttributes GrowthPerLevel,
    int StatusPointsPerLevel,
    CharacterFunctionProfile FunctionProfile,
    IReadOnlyList<EquipmentRef> StartingEquipment)
{
    // Motion job masks use sequential job bits; item specialization uses JobBitCode instead.
    public uint MotionJobMask => 1u << (JobCode - 1);

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
