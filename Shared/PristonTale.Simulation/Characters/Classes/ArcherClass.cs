using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class ArcherClass : CharacterClass
{
    public ArcherClass()
        : base(BuildDefinition(
            PlayerClass.Archer,
            "Archer",
            jobCode: 3,
            JobBitCode.Archer,
            CharacterBrood.Tempskron,
            bodyInx: "char\\tmABCD\\D001.inx",
            headInx: "char\\tmABCD\\tfh-D01.inx",
            armorTableIndex: 3,
            new CharacterAttributes(17, 11, 21, 27, 23),
            new CharacterFunctionProfile(80, 3, 3, 2, 3, 1, 0)))
    {
    }
}