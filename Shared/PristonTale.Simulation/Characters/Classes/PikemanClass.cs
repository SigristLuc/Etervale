using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class PikemanClass : CharacterClass
{
    public PikemanClass()
        : base(BuildDefinition(
            PlayerClass.Pikeman,
            "Pikeman",
            jobCode: 4,
            JobBitCode.Pikeman,
            CharacterBrood.Tempskron,
            bodyInx: "char\\tmABCD\\C001.inx",
            headInx: "char\\tmABCD\\tmh-c01.inx",
            armorTableIndex: 2,
            new CharacterAttributes(26, 9, 20, 19, 25),
            new CharacterFunctionProfile(75, 1, 3, 1, 1, 2, 0)))
    {
    }
}