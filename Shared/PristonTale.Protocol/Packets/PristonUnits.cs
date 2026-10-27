namespace PristonTale.Protocol.Packets;

// Fixed-point and angle conventions of the original engine: positions are world units << 8,
// angles are 4096 per turn and the yaw lives in angle[1].
public static class PristonUnits
{
    public const int FixedShift = 8;
    public const float FixedScale = 256f;
    public const int AngleTurn = 4096;

    public static int ToFixed(float value) => (int)MathF.Round(value * FixedScale);

    public static float FromFixed(int value) => value / FixedScale;

    public static short ToAngle(float radians)
    {
        var turn = radians / (2f * MathF.PI);
        return (short)((int)MathF.Round(turn * AngleTurn) & (AngleTurn - 1));
    }

    public static float FromAngle(short angle) => (angle & (AngleTurn - 1)) * (2f * MathF.PI / AngleTurn);
}
