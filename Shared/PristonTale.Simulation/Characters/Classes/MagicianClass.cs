using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class MagicianClass : CharacterClass
{
    public MagicianClass()
        : base(BuildDefinition(
            PlayerClass.Magician,
            "Magician",
            jobCode: 7,
            JobBitCode.Magician,
            CharacterBrood.Morion,
            bodyInx: "char\\tmABCD\\mD001.inx",
            headInx: "char\\tmABCD\\Mmh-D01.inx",
            armorTableIndex: 7,
            new CharacterAttributes(16, 29, 19, 14, 21),
            new CharacterFunctionProfile(190, 5, 1, 3, 3, 2, 1)))
    {
    }
}