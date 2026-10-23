using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Widgets;

internal static class HudJobTitles
{
    public static IReadOnlyList<string> For(PlayerClass characterClass) => JobTitles.For(characterClass);
}
