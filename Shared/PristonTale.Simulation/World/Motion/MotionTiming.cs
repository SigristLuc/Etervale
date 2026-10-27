using PristonTale.Contracts.Units;

namespace PristonTale.Simulation.World.Motion;

public static class MotionTiming
{
    public const float DamageRateFactor = 92f / 80f;

    public static float DurationMs(int rawFrames, float rate)
    {
        if (rawFrames <= 0)
        {
            return 0f;
        }

        return rawFrames * GameUnits.FrameMs / SafeRate(rate);
    }

    public static float EventOffsetMs(uint eventFrameTicks, float rate)
        => eventFrameTicks * GameUnits.FrameMs / (GameUnits.FrameTicks * SafeRate(rate));

    private static float SafeRate(float rate) => rate > 0f ? rate : 1f;
}
