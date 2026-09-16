using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering;

internal readonly record struct PristonSkyState(
    Vector3 SunDirection,
    Vector3 SunColor,
    Vector3 AmbientColor,
    Vector3 SkyColor,
    float LampFactor);

internal sealed class PristonDayNightCycle
{
    private DayNightSettings _settings;

    public PristonDayNightCycle(DayNightSettings settings = null)
    {
        _settings = settings ?? DayNightSettings.Default;
        DayLengthSeconds = _settings.DayLengthSeconds;
        TimeOfDay = _settings.StartTimeOfDay;
    }

    public void Configure(DayNightSettings settings)
    {
        _settings = settings ?? DayNightSettings.Default;
        DayLengthSeconds = _settings.DayLengthSeconds;
        _cachedTime = float.NaN;
    }

    public float DayLengthSeconds { get; set; }

    public float TimeOfDay { get; private set; }

    private PristonSkyState _cachedState;
    private float _cachedTime = float.NaN;

    public void Update(float deltaSeconds)
    {
        if (DayLengthSeconds <= 0f)
            return;
        var hoursPerSecond = 24f / DayLengthSeconds;
        TimeOfDay = Wrap24(TimeOfDay + deltaSeconds * hoursPerSecond);
    }

    public void SetTime(float hours) => TimeOfDay = Wrap24(hours);

    public void Advance(float hours) => TimeOfDay = Wrap24(TimeOfDay + hours);

    public PristonSkyState Evaluate()
    {
        if (TimeOfDay == _cachedTime)
            return _cachedState;
        _cachedTime = TimeOfDay;
        _cachedState = EvaluateUncached();
        return _cachedState;
    }

    private PristonSkyState EvaluateUncached()
    {
        // Sun arc: dayAngle = 0 at 06:00 (sunrise, east), PI/2 at noon, PI at 18:00 (sunset, west).
        var dayAngle = (TimeOfDay - 6f) / 12f * MathHelper.Pi;
        var elevation = MathF.Sin(dayAngle);
        var eastWest = MathF.Cos(dayAngle);

        // Keep the light above the horizon so directional shadows stay sane even at
        // night (the moon stands in for the sun; its colour is dim so it barely shows).
        var clampedElevation = MathF.Max(elevation, 0.25f);
        var toLight = Vector3.Normalize(new Vector3(eastWest, clampedElevation, -0.35f));
        var sunDirection = -toLight;

        var (sunColor, ambient, sky) = SampleGradient(TimeOfDay);

        // Lamps fade in as the sun drops: full on below the horizon, off when the
        // sun is well up. ~0.8 at dawn/dusk so lamps still read during transitions.
        var lampFactor = MathHelper.Clamp(1f - (elevation + 0.1f) * 2f, 0f, 1f);

        return new PristonSkyState(sunDirection, sunColor, ambient, sky, lampFactor);
    }

    private (Vector3 Sun, Vector3 Ambient, Vector3 Sky) SampleGradient(float hour)
    {
        var gradient = _settings.Gradient;
        for (var i = 0; i < gradient.Count - 1; i++)
        {
            var a = gradient[i];
            var b = gradient[i + 1];
            if (hour >= a.Hour && hour <= b.Hour)
            {
                var span = b.Hour - a.Hour;
                var t = span > 0f ? (hour - a.Hour) / span : 0f;
                return (Vector3.Lerp(a.Sun, b.Sun, t), Vector3.Lerp(a.Ambient, b.Ambient, t), Vector3.Lerp(a.Sky, b.Sky, t));
            }
        }

        var last = gradient[^1];
        return (last.Sun, last.Ambient, last.Sky);
    }

    private static float Wrap24(float hours) => ((hours % 24f) + 24f) % 24f;
}
