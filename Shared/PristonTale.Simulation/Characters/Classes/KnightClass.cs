using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class KnightClass : CharacterClass
{
    public KnightClass()
        : base(BuildDefinition(
            PlayerClass.Knight,
            "Knight",
            jobCode: 6,
            JobBitCode.Knight,
            CharacterBrood.Morion,
            bodyInx: "char\\tmABCD\\mA001.inx",
            headInx: "char\\tmABCD\\Mmh-A01.inx",
            armorTableIndex: 4,
            new CharacterAttributes(26, 13, 17, 19, 24),
            new CharacterFunctionProfile(190, 2, 2, 2, 1, 2, 0)))
    {
    }
}