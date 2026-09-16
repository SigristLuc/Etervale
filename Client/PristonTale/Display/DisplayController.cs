using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Display;

internal sealed class DisplayController
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly GameWindow _window;
    private readonly IClientLog _log;
    private readonly SdlDisplay _display;

    public DisplayController(GraphicsDeviceManager graphics, GameWindow window, IClientLog log)
    {
        _graphics = graphics;
        _window = window;
        _log = log;
        try
        {
            _display = new SdlDisplay();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _log.Error("[Display] SDL mode access unavailable; using the current monitor and desktop frequency.", exception);
        }
    }

    public string Apply(ClientSettings requested)
    {
        var width = requested.WindowWidth;
        var height = requested.WindowHeight;
        var monitor = 0;
        if (_display is not null)
        {
            monitor = _display.MonitorFor(_window.Handle);
            if (requested.Monitor >= 0 && requested.Monitor < _display.Count)
            {
                monitor = requested.Monitor;
            }
            else if (requested.Monitor >= 0)
            {
                _log.Warn($"[Display] Monitor {requested.Monitor} is unavailable; using {monitor}.");
            }

            // Leave fullscreen before moving the window to another monitor or choosing a new exclusive mode.
            if (_graphics.IsFullScreen)
            {
                _graphics.IsFullScreen = false;
                _graphics.ApplyChanges();
            }

            var bounds = _display.Bounds(monitor);
            _window.Position = new Point(bounds.X + 32, bounds.Y + 32);
            if (requested.WindowMode == WindowMode.Fullscreen)
            {
                var mode = SelectMode(requested, monitor);
                _display.Select(_window.Handle, mode);
                width = mode.Width;
                height = mode.Height;
            }
            else if (requested.WindowMode == WindowMode.Borderless)
            {
                var desktop = _display.Desktop(monitor);
                width = desktop.Width;
                height = desktop.Height;
            }
        }

        _graphics.PreferredBackBufferWidth = width;
        _graphics.PreferredBackBufferHeight = height;
        _graphics.HardwareModeSwitch = requested.WindowMode == WindowMode.Fullscreen;
        _graphics.IsFullScreen = requested.WindowMode != WindowMode.Windowed;
        _graphics.SynchronizeWithVerticalRetrace = requested.VerticalSync;
        _graphics.PreferMultiSampling = requested.MultiSampleCount > 0;
        _graphics.ApplyChanges();

        var parameters = _graphics.GraphicsDevice.PresentationParameters;
        var hz = _display?.Current(monitor).RefreshRate ?? 0;
        var frequency = hz > 0 ? $"{hz} Hz" : "Hz unavailable";
        var effective = $"Monitor {monitor}: {parameters.BackBufferWidth}x{parameters.BackBufferHeight}, {frequency}, MSAA {parameters.MultiSampleCount}x";
        _log.Info($"[Display] requested={requested.WindowMode} {requested.WindowWidth}x{requested.WindowHeight}@{requested.RefreshRate}; effective={effective}");
        if (parameters.MultiSampleCount != requested.MultiSampleCount)
        {
            _log.Warn($"[Display] Requested MSAA {requested.MultiSampleCount}x; device selected {parameters.MultiSampleCount}x.");
        }

        return effective;
    }

    public void DescribeOptions(PresentationSettings shared)
    {
        if (_display is null)
        {
            return;
        }

        shared.MonitorCount = _display.Count;
        var options = new List<DisplayModeOption>();
        for (var monitor = 0; monitor < _display.Count; monitor++)
        {
            foreach (var mode in _display.Modes(monitor))
            {
                var option = new DisplayModeOption(monitor, mode.Width, mode.Height, mode.RefreshRate);
                if (mode.Width >= 800 && mode.Height >= 600 && !options.Contains(option))
                {
                    options.Add(option);
                }
            }
        }

        shared.DisplayModes = options;
    }

    private SdlDisplay.Mode SelectMode(ClientSettings requested, int monitor)
    {
        var desktop = _display.Desktop(monitor);
        var selected = desktop;
        var selectedDistance = int.MaxValue;
        var targetHz = requested.RefreshRate == 0 ? desktop.RefreshRate : requested.RefreshRate;
        foreach (var mode in _display.Modes(monitor))
        {
            if (mode.Width != requested.WindowWidth || mode.Height != requested.WindowHeight)
            {
                continue;
            }

            var distance = Math.Abs(mode.RefreshRate - targetHz);
            if (distance < selectedDistance)
            {
                selected = mode;
                selectedDistance = distance;
            }
        }

        if (selectedDistance != 0)
        {
            _log.Warn($"[Display] Requested fullscreen mode unavailable; selecting {selected.Width}x{selected.Height}@{selected.RefreshRate}.");
        }

        return selected;
    }
}
