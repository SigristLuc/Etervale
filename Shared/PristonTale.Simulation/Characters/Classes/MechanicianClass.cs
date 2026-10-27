using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class MechanicianClass : CharacterClass
{
    public MechanicianClass()
        : base(BuildDefinition(
            PlayerClass.Mechanician,
            "Mechanician",
            CharacterBrood.Tempskron,
            bodyInx: "char\\tmABCD\\A001.inx",
            headInx: "char\\tmABCD\\tmh-a01.inx",
            armorTableIndex: 0,
            new CharacterAttributes(24, 8, 25, 18, 24),
            new CharacterFunctionProfile(65, 2, 2, 2, 2, 2, 0)))
    {
    }
}