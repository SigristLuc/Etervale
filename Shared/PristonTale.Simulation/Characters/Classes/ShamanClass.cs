using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class ShamanClass : CharacterClass
{
    public ShamanClass()
        : base(BuildDefinition(
            PlayerClass.Shaman,
            "Shaman",
            CharacterBrood.Morion,
            bodyInx: "char\\tmABCD\\me001.inx",
            headInx: "char\\tmABCD\\Mmh-E01.inx",
            armorTableIndex: 9,
            new CharacterAttributes(15, 27, 20, 15, 22),
            new CharacterFunctionProfile(190, 5, 1, 3, 3, 2, 1)))
    {
    }
}
