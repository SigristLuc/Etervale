using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Characters;

public abstract class CharacterClass
{
    private readonly List<Skill> _skills = new();

    protected CharacterClass(CharacterClassDef definition)
    {
        Definition = definition;
        DefineSkills(_skills);
    }

    public CharacterClassDef Definition { get; }
    public PlayerClass Class => Definition.Class;
    public string Name => Definition.Name;
    public CharacterBrood Brood => Definition.Brood;
    public IReadOnlyList<Skill> Skills => _skills;

    protected abstract void DefineSkills(IList<Skill> skills);

    protected static CharacterClassDef BuildDefinition(
        PlayerClass playerClass,
        string name,
        int jobCode,
        JobBitCode jobBitCode,
        CharacterBrood brood,
        string bodyInx,
        string headInx,
        int armorTableIndex,
        CharacterAttributes baseAttributes,
        CharacterFunctionProfile functionProfile) =>
        new(
            playerClass,
            name,
            jobCode,
            jobBitCode,
            brood,
            new CharacterVisualSet(
                new CharacterAssetRef(bodyInx),
                new CharacterAssetRef(headInx),
                FaceIndex: 0,
                HairIndex: 0,
                armorTableIndex),
            CharacterMotionName.Stand,
            baseAttributes,
            CharacterAttributes.Empty,
            StatusPointsPerLevel: 5,
            functionProfile,
            []);

    public virtual void OnSpawn(PlayerCharacter player) { }

    public virtual void OnLevelUp(PlayerCharacter player) { }
}
