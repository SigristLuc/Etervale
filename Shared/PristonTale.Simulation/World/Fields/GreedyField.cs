namespace PristonTale.Simulation.World.Fields;

public static class GreedyField
{
    public static readonly FieldDef Definition = new(
        "Greedy", "Greedy", FieldState.Ice, FieldBackdrops.Snow,
        "Field\\Greedy\\Greedy.smd", [])
    {
        FieldCode = 34,
        Center = new FieldPoint(13930, 23206),
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}
