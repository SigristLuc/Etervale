using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

internal sealed class HudSettingsPanel
{
    private static readonly int[] FrameLimits = [0, 30, 60, 90, 120, 144, 165, 240, 360];
    private static readonly int[] Samples = [0, 2, 4, 8];
    private static readonly int[] ShadowSizes = [1024, 2048, 4096, 8192];
    private static readonly int[] LampSizes = [256, 512, 1024];
    private static readonly int[] ShadowUpdateRates = [30, 60, 120, 0];
    private static readonly Point[] WindowSizes = [new(800, 600), new(1024, 768), new(1280, 720), new(1920, 1080), new(2560, 1440)];
    private readonly PresentationSettings _settings;
    private readonly string[] _labels = new string[15];
    private ClientSettings _labelSettings;

    public HudSettingsPanel(PresentationSettings settings) => _settings = settings;

    public void Click(Rectangle panel, Point pointer, bool backwards)
    {
        if (_settings.AwaitingConfirmation)
        {
            return;
        }

        var current = _settings.Requested;
        for (var row = 0; row < _labels.Length; row++)
        {
            if (!Row(panel, row).Contains(pointer))
            {
                continue;
            }

            var direction = backwards ? -1 : 1;
            _settings.Requested = row switch
            {
                0 => current with { WindowMode = (WindowMode)(((int)current.WindowMode + direction + 3) % 3) },
                1 => current with { Monitor = ((current.Monitor + 1 + direction + _settings.MonitorCount + 1) % (_settings.MonitorCount + 1)) - 1 },
                2 => NextResolution(current, direction),
                3 => current with { RefreshRate = NextFrequency(current, direction) },
                4 => current with { VerticalSync = !current.VerticalSync },
                5 => current with { MusicEnabled = !current.MusicEnabled },
                6 => current with { SoundEnabled = !current.SoundEnabled },
                7 => current with { MaxFps = Cycle(FrameLimits, current.MaxFps, direction) },
                8 => current with { BackgroundMaxFps = Cycle(FrameLimits, current.BackgroundMaxFps, direction) },
                9 => current with { MultiSampleCount = Cycle(Samples, current.MultiSampleCount, direction) },
                10 => current with { ShadowMapSize = Cycle(ShadowSizes, current.ShadowMapSize, direction) },
                11 => current with { LampShadowTileSize = Cycle(LampSizes, current.LampShadowTileSize, direction) },
                12 => current with { ShadowUpdateRate = Cycle(ShadowUpdateRates, current.ShadowUpdateRate, direction) },
                13 => current with { ShadowsEnabled = !current.ShadowsEnabled },
                14 => current with { UiScale = Math.Clamp(MathF.Round(current.UiScale + direction * 0.1f, 2), 0.75f, 1.5f) },
                _ => current
            };
            return;
        }
    }

    public void Draw(HudCanvas canvas, Rectangle panel)
    {
        RefreshLabels();
        for (var index = 0; index < _labels.Length; index++)
        {
            var row = Row(panel, index);
            canvas.Frame(row, "Button", 5);
            canvas.Text(_labels[index], row, scale: 0.62f, centered: true);
        }

        var effective = new Rectangle(panel.X + 18, panel.Bottom - 58, panel.Width - 36, 20);
        canvas.Text(_settings.EffectiveDisplay, effective, HudCanvas.Muted, 0.5f, centered: true);
        var hint = new Rectangle(panel.X + 18, panel.Bottom - 32, panel.Width - 36, 20);
        if (_settings.Message.Length > 0)
        {
            canvas.Text("Settings need attention; see the client log.", hint, Color.Orange, 0.53f, centered: true);
            return;
        }

        canvas.Text("Click: next   Shift+click: previous", hint, HudCanvas.Muted, 0.53f, centered: true);
    }

    private static Rectangle Row(Rectangle panel, int row)
    {
        var step = Math.Min(28, (panel.Height - 106) / 15);
        return new Rectangle(panel.X + 18, panel.Y + 46 + row * step, panel.Width - 36, step - 3);
    }

    private ClientSettings NextResolution(ClientSettings current, int direction)
    {
        var sizes = current.WindowMode == WindowMode.Fullscreen ? new List<Point>() : new List<Point>(WindowSizes);
        foreach (var mode in _settings.DisplayModes)
        {
            var size = new Point(mode.Width, mode.Height);
            if ((current.Monitor == -1 || mode.Monitor == current.Monitor) && !sizes.Contains(size))
            {
                sizes.Add(size);
            }
        }

        sizes.Sort(static (left, right) => ((long)left.X * left.Y).CompareTo((long)right.X * right.Y));
        if (sizes.Count == 0)
        {
            return current;
        }

        var index = sizes.IndexOf(new Point(current.WindowWidth, current.WindowHeight));
        var next = sizes[(index + direction + sizes.Count) % sizes.Count];
        return current with { WindowWidth = next.X, WindowHeight = next.Y };
    }

    private int NextFrequency(ClientSettings current, int direction)
    {
        if (current.WindowMode != WindowMode.Fullscreen)
        {
            return 0;
        }

        var frequencies = new List<int> { 0 };
        foreach (var mode in _settings.DisplayModes)
        {
            if ((current.Monitor == -1 || mode.Monitor == current.Monitor)
                && mode.Width == current.WindowWidth && mode.Height == current.WindowHeight && !frequencies.Contains(mode.RefreshRate))
            {
                frequencies.Add(mode.RefreshRate);
            }
        }

        frequencies.Sort();
        return Cycle(frequencies, current.RefreshRate, direction);
    }

    private static int Cycle(IReadOnlyList<int> values, int value, int direction)
    {
        var index = 0;
        for (var candidate = 0; candidate < values.Count; candidate++)
        {
            if (values[candidate] == value)
            {
                index = candidate;
                break;
            }
        }

        return values[(index + direction + values.Count) % values.Count];
    }

    private void RefreshLabels()
    {
        var settings = _settings.Requested;
        if (_labelSettings == settings)
        {
            return;
        }

        _labelSettings = settings;
        _labels[0] = $"Window: {settings.WindowMode}";
        _labels[1] = settings.Monitor < 0 ? "Monitor: current" : $"Monitor: {settings.Monitor}";
        _labels[2] = $"Resolution: {settings.WindowWidth} x {settings.WindowHeight}";
        _labels[3] = settings.WindowMode != WindowMode.Fullscreen ? "Frequency: desktop" : $"Frequency: {settings.RefreshRate} Hz (0 = automatic)";
        _labels[4] = settings.VerticalSync ? "VSync: on" : "VSync: off";
        _labels[5] = settings.MusicEnabled ? "BGM: on" : "BGM: off";
        _labels[6] = settings.SoundEnabled ? "Audio: on" : "Audio: off";
        _labels[7] = settings.MaxFps == 0 ? "Maximum FPS: unlimited" : $"Maximum FPS: {settings.MaxFps}";
        _labels[8] = settings.BackgroundMaxFps == 0 ? "Background FPS: foreground limit" : $"Background FPS: {settings.BackgroundMaxFps}";
        _labels[9] = $"MSAA: {settings.MultiSampleCount}x (0 = off)";
        _labels[10] = $"Sun shadow: {settings.ShadowMapSize}";
        _labels[11] = $"Lamp shadow: {settings.LampShadowTileSize}";
        _labels[12] = settings.ShadowUpdateRate == 0 ? "Shadow animation: every frame" : $"Shadow animation: {settings.ShadowUpdateRate} Hz";
        _labels[13] = settings.ShadowsEnabled ? "Shadows: on" : "Shadows: off";
        _labels[14] = $"Interface scale: {settings.UiScale:F2}";
    }
}
