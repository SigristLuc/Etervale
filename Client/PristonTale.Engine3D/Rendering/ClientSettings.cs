using System.Text.Json.Serialization;
using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Engine3D.Rendering;

[JsonConverter(typeof(JsonStringEnumConverter<WindowMode>))]
public enum WindowMode
{
    Windowed,
    Borderless,
    Fullscreen
}

public sealed record ClientSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public int Monitor { get; init; } = -1;
    public int WindowWidth { get; init; } = 1280;
    public int WindowHeight { get; init; } = 720;
    public WindowMode WindowMode { get; init; }
    public int RefreshRate { get; init; }
    public bool VerticalSync { get; init; }
    public bool MusicEnabled { get; init; } = true;
    public bool SoundEnabled { get; init; } = true;
    public int MaxFps { get; init; }
    public int BackgroundMaxFps { get; init; } = 30;
    public float UiScale { get; init; } = 1f;
    public int MultiSampleCount { get; init; } = 8;
    public int ShadowMapSize { get; init; } = 8192;
    public int LampShadowTileSize { get; init; } = 1024;
    public int ShadowUpdateRate { get; init; } = 60;
    public bool ShadowsEnabled { get; init; } = true;

    public ClientSettings Validate(IClientLog log)
    {
        var defaults = new ClientSettings();
        var valid = this with
        {
            Monitor = Monitor >= -1 ? Monitor : defaults.Monitor,
            WindowWidth = WindowWidth is >= 800 and <= 16384 ? WindowWidth : defaults.WindowWidth,
            WindowHeight = WindowHeight is >= 600 and <= 16384 ? WindowHeight : defaults.WindowHeight,
            WindowMode = Enum.IsDefined(WindowMode) ? WindowMode : defaults.WindowMode,
            RefreshRate = RefreshRate is >= 0 and <= 1000 ? RefreshRate : defaults.RefreshRate,
            MaxFps = IsValidFps(MaxFps) ? MaxFps : defaults.MaxFps,
            BackgroundMaxFps = IsValidFps(BackgroundMaxFps) ? BackgroundMaxFps : defaults.BackgroundMaxFps,
            UiScale = float.IsFinite(UiScale) && UiScale is >= 0.75f and <= 1.5f ? UiScale : defaults.UiScale,
            MultiSampleCount = MultiSampleCount is 0 or 2 or 4 or 8 ? MultiSampleCount : defaults.MultiSampleCount,
            ShadowMapSize = ShadowMapSize is 1024 or 2048 or 4096 or 8192 ? ShadowMapSize : defaults.ShadowMapSize,
            LampShadowTileSize = LampShadowTileSize is 256 or 512 or 1024 ? LampShadowTileSize : defaults.LampShadowTileSize,
            ShadowUpdateRate = ShadowUpdateRate is 0 or 30 or 60 or 120 ? ShadowUpdateRate : defaults.ShadowUpdateRate
        };

        if (valid != this)
        {
            log.Warn("[Settings] Invalid presentation values replaced with defaults.");
        }

        return valid;
    }

    public bool HasSameDisplay(ClientSettings other)
    {
        return Monitor == other.Monitor
            && WindowWidth == other.WindowWidth
            && WindowHeight == other.WindowHeight
            && WindowMode == other.WindowMode
            && RefreshRate == other.RefreshRate
            && MultiSampleCount == other.MultiSampleCount;
    }

    public int FrameLimit(bool active)
    {
        if (active || BackgroundMaxFps == 0)
        {
            return MaxFps;
        }

        return MaxFps == 0 ? BackgroundMaxFps : Math.Min(MaxFps, BackgroundMaxFps);
    }

    private static bool IsValidFps(int value) => value == 0 || value is >= 15 and <= 1000;
}
