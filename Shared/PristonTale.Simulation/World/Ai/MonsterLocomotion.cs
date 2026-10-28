using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.World.Ai;

public static class MonsterLocomotion
{
    private const float BaseUnitsPerSecond = 70f;

    public static float UnitsPerSecond(MonsterDefinition definition, bool running)
    {
        int steps = definition.IsBoss && definition.SoundCode == CharacterSoundCodes.DarkKnight ? 2 : 1;
        if (running)
        {
            steps += definition.SoundCode switch
            {
                CharacterSoundCodes.Greven => 4,
                CharacterSoundCodes.Wolverine => 2,
                _ => 1
            };
        }

        return BaseUnitsPerSecond * steps;
    }
}
