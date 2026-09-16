namespace PristonTale.Simulation.World.Fields;

public static class ForeverFall4Field
{
    public static readonly FieldDef Definition = new(
        "forever-fall-04", "Forever Fall 4", FieldState.Forest, FieldBackdrops.Fall,
        "Field\\forever-fall\\forever-fall-04.smd", [])
    {
        FieldCode = 17,
        Center = new FieldPoint(-1583, 37905),
        StartPoints = [new FieldPoint(-2135, 33668), new FieldPoint(4073, 32119)],
        Gates = [new FieldGate("forever-fall-03", -2949, 40442, 0)],
        Ambients =
        [
            new FieldAmbient(new FieldVector(-1448, 827, 34188), 160, 7),
            new FieldAmbient(new FieldVector(618, 531, 35981), 160, 7),
            new FieldAmbient(new FieldVector(2282, 537, 32355), 160, 20),
            new FieldAmbient(new FieldVector(2597, 793, 30745), 0, 20),
            new FieldAmbient(new FieldVector(2562, 641, 32726), 0, 20),
            new FieldAmbient(new FieldVector(3789, 770, 30062), 0, 20),
            new FieldAmbient(new FieldVector(3698, 909, 34179), 60, 14)
        ],
        BackMusicCode = "BGM_CODE_FOREST",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class ForeverFall3Field
{
    public static readonly FieldDef Definition = new(
        "forever-fall-03", "Forever Fall 3", FieldState.Forest, FieldBackdrops.Fall,
        "Field\\forever-fall\\forever-fall-03.smd", [])
    {
        FieldCode = 18,
        Center = new FieldPoint(-5823, 43244),
        StartPoints = [new FieldPoint(-1516, 45437), new FieldPoint(-3586, 42886)],
        Gates = [new FieldGate("forever-fall-02", -2349, 49830, 0)],
        WarpGates = [new FieldWarpGate(-6306, 43241, 779, 64, 32, "Mcave", 119025, 35680, 499, 0, 0)],
        Ambients =
        [
            new FieldAmbient(new FieldVector(-3573, 861, 42603), 0, 15),
            new FieldAmbient(new FieldVector(-2160, 943, 42588), 0, 19)
        ],
        BackMusicCode = "BGM_CODE_FOREST",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class ForeverFall2Field
{
    public static readonly FieldDef Definition = new(
        "forever-fall-02", "Forever Fall 2", FieldState.Forest, FieldBackdrops.Fall,
        "Field\\forever-fall\\forever-fall-02.smd", [])
    {
        FieldCode = 19,
        Center = new FieldPoint(1691, 52599),
        StartPoints =
        [
            new FieldPoint(-1024, 45437),
            new FieldPoint(-1543, 52333),
            new FieldPoint(-1310, 54945)
        ],
        Gates = [new FieldGate("forever-fall-01", 667, 59371, 0)],
        Ambients =
        [
            new FieldAmbient(new FieldVector(-266, 862, 57971), 100, 13),
            new FieldAmbient(new FieldVector(-5014, 629, 56702), 100, 13),
            new FieldAmbient(new FieldVector(2153, 772, 53576), 80, 13),
            new FieldAmbient(new FieldVector(-395, 542, 56521), 80, 13),
            new FieldAmbient(new FieldVector(-3329, 522, 56469), 20, 15),
            new FieldAmbient(new FieldVector(-39, 604, 54080), 10, 6)
        ],
        BackMusicCode = "BGM_CODE_FOREST",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class ForeverFall1Field
{
    public static readonly FieldDef Definition = new(
        "forever-fall-01", "Forever Fall 1", FieldState.Forest, FieldBackdrops.Fall,
        "Field\\forever-fall\\forever-fall-01.smd", [])
    {
        FieldCode = 20,
        Center = new FieldPoint(2032, 71183),
        StartPoints = [new FieldPoint(1875, 69871), new FieldPoint(2637, 60219)],
        Gates = [new FieldGate("pilai", -8508, -10576, 0)],
        WarpGates = [new FieldWarpGate(1962, 71184, 559, 64, 32, "pilai", 1993, 73134, 449, 0, 1)],
        Ambients =
        [
            new FieldAmbient(new FieldVector(2849, 577, 67330), 30, 18),
            new FieldAmbient(new FieldVector(317, 653, 64957), 10, 19),
            new FieldAmbient(new FieldVector(-1166, 779, 63268), 40, 18),
            new FieldAmbient(new FieldVector(-181, 864, 62576), 10, 18),
            new FieldAmbient(new FieldVector(190, 686, 61850), 10, 18),
            new FieldAmbient(new FieldVector(-1070, 671, 61561), 10, 18),
            new FieldAmbient(new FieldVector(1815, 562, 65414), 100, 8),
            new FieldAmbient(new FieldVector(1800, 603, 61149), 100, 8)
        ],
        BackMusicCode = "BGM_CODE_FOREST",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
