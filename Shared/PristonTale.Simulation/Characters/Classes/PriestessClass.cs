using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class PriestessClass : CharacterClass
{
    public PriestessClass()
        : base(BuildDefinition(
            PlayerClass.Priestess,
            "Priestess",
            jobCode: 8,
            JobBitCode.Priestess,
            CharacterBrood.Morion,
            bodyInx: "char\\tmABCD\\mC001.inx",
            headInx: "char\\tmABCD\\Mfh-C01.inx",
            armorTableIndex: 6,
            new CharacterAttributes(15, 28, 21, 15, 20),
            new CharacterFunctionProfile(200, 4, 1, 3, 3, 2, 2)))
    {
    }
}