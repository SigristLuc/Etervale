using PristonTale.Contracts.World;

namespace PristonTale.Simulation.World;

// The original's rain on the world clock: in every cycle of 8192 game minutes (about 1 h 49 min) the sky clouds over at
// minute 512, rains from 60 minutes later until 60 minutes before minute 932 (4 real minutes), then clears; never in a
// dungeon, a room, an ice field or Bellatra. The original's rain was always the same; here each cycle's number draws its
// rain (light, steady or heavy, or a storm) and its wind, so every server and client restart agrees on it.
public static class WeatherSchedule
{
    public const int DefaultCycleMinutes = 8192;
    public const int CloudsMinute = 512;
    public const int CloudyMinutes = 420;
    public const int RampMinutes = 60;
    // A cycle needs room for the cloudy spell.
    public const int MinimumCycleMinutes = CloudsMinute + CloudyMinutes + 1;

    // Light, steady and heavy rain; a storm rains at the most.
    public const byte LightRain = 96;
    public const byte SteadyRain = 160;
    public const byte HeavyRain = 224;
    public const byte StormRain = 255;

    public static bool Rains(FieldDef field) =>
        field.State is not (FieldState.Dungeon or FieldState.Room or FieldState.Ice) && field.FieldCode != FieldCodes.Bellatra;

    // The weather at a game clock reading (game milliseconds), for a cycle of cycleMinutes game minutes (at least
    // MinimumCycleMinutes).
    public static WeatherState At(long gameMs, int cycleMinutes = DefaultCycleMinutes)
    {
        var minutes = Math.Max(0L, gameMs / WorldClock.MinuteMs);
        var minute = minutes % cycleMinutes;
        if (minute <= CloudsMinute || minute >= CloudsMinute + CloudyMinutes)
        {
            return WeatherState.Clear;
        }

        var draw = Mix((ulong)(minutes / cycleMinutes));
        var heading = (byte)(draw >> 8);
        var raining = minute > CloudsMinute + RampMinutes && minute < CloudsMinute + CloudyMinutes - RampMinutes;
        var (kind, intensity, wind) = (draw % 100) switch
        {
            < 30 => (WeatherKind.Rain, LightRain, 16 + (int)((draw >> 16) % 64)),
            < 65 => (WeatherKind.Rain, SteadyRain, 32 + (int)((draw >> 16) % 80)),
            < 85 => (WeatherKind.Rain, HeavyRain, 64 + (int)((draw >> 16) % 96)),
            _ => (WeatherKind.Storm, StormRain, 128 + (int)((draw >> 16) % 128))
        };
        return raining
            ? new WeatherState(kind, intensity, heading, (byte)wind)
            : new WeatherState(WeatherKind.Overcast, 0, heading, (byte)(wind / 2));
    }

    // SplitMix64's finaliser: one well-spread draw per cycle number.
    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
