using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class FighterClass : CharacterClass
{
    public FighterClass()
        : base(BuildDefinition(
            PlayerClass.Fighter,
            "Fighter",
            CharacterBrood.Tempskron,
            bodyInx: "char\\tmABCD\\B001.inx",
            headInx: "char\\tmABCD\\tmh-b01.inx",
            armorTableIndex: 1,
            new CharacterAttributes(28, 6, 21, 17, 27),
            new CharacterFunctionProfile(70, 1, 3, 1, 1, 2, 0)))
    {
    }
}