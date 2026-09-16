namespace PristonTale.Simulation.World.Fields;

public static class Sod1Field
{
    public static readonly FieldDef Definition = new(
        "sod-1", "SoD 1", FieldState.Ruin, FieldBackdrops.Desert,
        "Field\\Sod\\sod-1.smd", [])
    {
        FieldCode = 30,
        Center = new FieldPoint(22018, 6569),
        BackMusicCode = "BGM_CODE_CUSTOM",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
