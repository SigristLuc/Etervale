using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Display;

internal sealed class PresentationController
{
    private readonly PresentationSettings _shared;
    private readonly ClientSettingsStore _store;
    private readonly DisplayController _display;
    private readonly GraphicsDeviceManager _graphics;
    private readonly FramePacer _pacer = new();
    private ClientSettings _applied;
    private ClientSettings _accepted;
    private long _confirmationDeadline;

    public PresentationController(PresentationSettings shared, ClientSettingsStore store, GraphicsDeviceManager graphics)
    {
        _shared = shared;
        _store = store;
        _graphics = graphics;
        _display = new DisplayController(graphics, ConsoleClientLog.Instance);
        DisplayController.DescribeOptions(shared);
        _accepted = shared.Requested;
        Apply(shared.Requested);
        if (_applied.WindowMode != WindowMode.Windowed)
        {
            _accepted = new ClientSettings();
            _shared.AwaitingConfirmation = true;
            _shared.ConfirmationSeconds = 15;
        }
    }

    public void UpdateBoundary()
    {
        var requested = _shared.Requested;
        if (requested != _applied && !_shared.AwaitingConfirmation)
        {
            requested = requested.Validate(ConsoleClientLog.Instance);
            _shared.Requested = requested;
            var preview = !requested.HasSameDisplay(_applied);
            Apply(requested);
            if (preview && _applied == requested)
            {
                _shared.AwaitingConfirmation = true;
                _confirmationDeadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 15;
            }
            else
            {
                Accept();
            }
        }

        if (_shared.AwaitingConfirmation)
        {
            if (_confirmationDeadline == 0)
            {
                _confirmationDeadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 15;
            }

            _shared.ConfirmationSeconds = Math.Max(0,
                (int)Math.Ceiling((double)(_confirmationDeadline - Stopwatch.GetTimestamp()) / Stopwatch.Frequency));
            if (_shared.RevertRequested || _shared.ConfirmationSeconds == 0)
            {
                Apply(_accepted);
                _shared.Requested = _accepted;
                _shared.AwaitingConfirmation = false;
            }
            else if (_shared.ConfirmRequested)
            {
                Accept();
            }
        }

        _shared.ConfirmRequested = false;
        _shared.RevertRequested = false;
        if (_store.LastError.Length > 0)
        {
            _shared.Message = _store.LastError;
        }
    }

    public void CaptureWindowSize(int width, int height)
    {
        if (_applied is null || _shared.AwaitingConfirmation || _applied.WindowMode != WindowMode.Windowed
            || width < 800 || height < 600 || _shared.Requested != _applied)
        {
            return;
        }

        if (width == _applied.WindowWidth && height == _applied.WindowHeight)
        {
            return;
        }

        _applied = _applied with { WindowWidth = width, WindowHeight = height };
        _shared.Requested = _applied;
        Accept();
    }

    public long Pace(bool active)
    {
        var started = Stopwatch.GetTimestamp();
        var deadline = _pacer.Schedule(started, Stopwatch.Frequency, _applied.FrameLimit(active));
        var now = started;
        while (now < deadline)
        {
            var remainingMs = (deadline - now) * 1000d / Stopwatch.Frequency;
            Thread.Sleep(Math.Clamp((int)Math.Ceiling(remainingMs), 1, 4));
            now = Stopwatch.GetTimestamp();
        }

        return now - started;
    }

    public Task CompleteAsync() => _store.CompleteAsync();

    private void Apply(ClientSettings settings)
    {
        _shared.Requested = settings;
        try
        {
            if (_applied is null || !settings.HasSameDisplay(_applied))
            {
                _shared.EffectiveDisplay = _display.Apply(settings);
            }
            else if (settings.VerticalSync != _applied.VerticalSync)
            {
                _graphics.SynchronizeWithVerticalRetrace = settings.VerticalSync;
                _graphics.ApplyChanges();
            }

            _shared.ApplyRendering(settings);
            _applied = settings;
            _shared.Message = string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NoSuitableGraphicsDeviceException)
        {
            ConsoleClientLog.Instance.Error("[Display] Requested configuration failed; restoring the accepted settings.", exception);
            var fallback = _applied is null ? new ClientSettings() : _accepted;
            // PreparingDeviceSettings reads Requested, so rollback must publish it before ApplyChanges.
            _shared.Requested = fallback;
            _shared.EffectiveDisplay = _display.Apply(fallback);
            _shared.ApplyRendering(fallback);
            _shared.Message = exception.Message;
            _applied = fallback;
        }
    }

    private void Accept()
    {
        _accepted = _applied;
        _shared.Requested = _applied;
        _shared.AwaitingConfirmation = false;
        _store.QueueSave(_accepted);
    }
}
