
namespace PristonTale.Simulation.Characters.Classes;

public sealed class AssassinClass : CharacterClass
{
    public AssassinClass()
        : base(BuildDefinition(
            PlayerClass.Assassin,
            "Assassin",
            jobCode: 9,
            JobBitCode.Assassin,
            CharacterBrood.Tempskron,
            bodyInx: "char\\tmABCD\\e001.inx",
            headInx: "char\\tmABCD\\tfh-e01.inx",
            armorTableIndex: 8,
            new CharacterAttributes(25, 10, 22, 20, 22),
            new CharacterFunctionProfile(80, 3, 3, 2, 3, 1, 0)))
    {
    }
}
