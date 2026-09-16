namespace PristonTale.Simulation.World.Motion;

public static class MotionStateCode
{
    public const int Stand = 0x40;
    public const int Walk = 0x50;
    public const int Run = 0x60;
    public const int Attack = 0x100;
    public const int Damage = 0x110;
    public const int Dead = 0x120;
    public const int Eat = 0x140;
    public const int Skill = 0x150;
    public const int Restart = 0x200;
}

public readonly record struct MotionClip(
    int StartFrame,
    int EndFrame,
    uint Event0,
    uint Event1,
    uint Event2,
    uint Event3,
    bool Repeat)
{
    public int Frames => EndFrame - StartFrame;

    public int SpanTicks => Frames * MotionTiming.TicksPerFrame;

    public bool TryGetEventTicks(int index, out uint ticks)
    {
        ticks = index switch
        {
            0 => Event0,
            1 => Event1,
            2 => Event2,
            3 => Event3,
            _ => 0,
        };

        if (ticks == 0 || ticks > (uint)SpanTicks)
        {
            ticks = 0;
            return false;
        }

        return true;
    }
}
