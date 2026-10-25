namespace PristonTale.Simulation.World;

// The game's time of day, kept by the server and followed by every client's sky: one game minute lasts 800 ms (a day lasts
// 19.2 real minutes); day runs from 4:00 to 23:00 and night the rest. The clock counts game milliseconds; the server's
// count is the Unix time plus its configured offset, so every server and restart agree on the hour.
public static class WorldClock
{
    public const int MinuteMs = 800;
    public const int MinutesPerDay = 24 * 60;
    public const int DayStartHour = 4;
    public const int NightStartHour = 23;

    public static float HoursAt(long gameMs)
    {
        var minutes = gameMs / (double)MinuteMs % MinutesPerDay;
        if (minutes < 0)
        {
            minutes += MinutesPerDay;
        }

        return (float)(minutes / 60.0);
    }

    public static bool IsNight(float hours) => hours < DayStartHour || hours >= NightStartHour;
}

// A field's event: nightmare fields spawn their monsters only at night, and their people turn into the night creature.
public enum FieldEventKind
{
    None,
    Nightmare
}
