namespace PristonTale.Simulation.World.Fields;

public static class LostField
{
    public static readonly FieldDef Definition = new(
        "lost", "Lost Island", FieldState.Ruin, FieldBackdrops.Desert,
        "Field\\Lost\\lostisland.smd", [])
    {
        FieldCode = 37,
        Center = new FieldPoint(-12555, -1113),
        Gates = [new FieldGate("Losttemple", -11586, 7704, 0)],
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class LosttempleField
{
    public static readonly FieldDef Definition = new(
        "Losttemple", "Lost Temple", FieldState.Ruin, FieldBackdrops.Desert,
        "Field\\Losttemple\\lost_temple.smd", [])
    {
        FieldCode = 38,
        Center = new FieldPoint(-11718, 10794),
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
