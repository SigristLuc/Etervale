using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Simulation.World.Ai;

public static class MonsterLocomotion
{
    private const int Greven = 0x11A0;
    private const int Wolverlin = 0x11E0;
    private const int DarkKnight = 0x1460;
    private const float BaseUnitsPerSecond = 70f;

    public static float UnitsPerSecond(MonsterDefinition definition, bool running)
    {
        int steps = definition.IsBoss && definition.SoundCode == DarkKnight ? 2 : 1;
        if (running)
        {
            steps += definition.SoundCode switch
            {
                Greven => 4,
                Wolverlin => 2,
                _ => 1
            };
        }

        return BaseUnitsPerSecond * steps;
    }
}
