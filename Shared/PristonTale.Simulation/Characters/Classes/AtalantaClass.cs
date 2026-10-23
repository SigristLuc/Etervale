using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class AtalantaClass : CharacterClass
{
    public AtalantaClass()
        : base(BuildDefinition(
            PlayerClass.Atalanta,
            "Atalanta",
            jobCode: 5,
            JobBitCode.Atalanta,
            CharacterBrood.Morion,
            bodyInx: "char\\tmABCD\\mB001.inx",
            headInx: "char\\tmABCD\\Mfh-B01.inx",
            armorTableIndex: 5,
            new CharacterAttributes(23, 15, 19, 19, 23),
            new CharacterFunctionProfile(200, 2, 2, 2, 2, 1, 0)))
    {
    }
}