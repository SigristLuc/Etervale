using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace PristonTale.Display;

internal sealed class SdlDisplay
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Mode
    {
        public uint Format;
        public int Width;
        public int Height;
        public int RefreshRate;
        public IntPtr DriverData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayBounds
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CountDisplays();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WindowDisplay(IntPtr window);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CountModes(int display);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetMode(int display, int index, out Mode mode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CurrentMode(int display, out Mode mode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetBounds(int display, out DisplayBounds bounds);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetMode(IntPtr window, ref Mode mode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetError();

    private readonly CountDisplays _count;
    private readonly WindowDisplay _windowDisplay;
    private readonly CountModes _countModes;
    private readonly GetMode _getMode;
    private readonly CurrentMode _currentMode;
    private readonly CurrentMode _desktopMode;
    private readonly GetBounds _getBounds;
    private readonly SetMode _setMode;
    private readonly GetError _getError;

    public SdlDisplay()
    {
        var libraryName = OperatingSystem.IsWindows() ? "SDL2.dll" : "libSDL2-2.0.so.0";
        // Resolve against MonoGame's assembly/dependencies, matching the SDL binary that owns the window.
        var library = NativeLibrary.Load(libraryName, typeof(Game).Assembly, null);
        _count = Load<CountDisplays>(library, "SDL_GetNumVideoDisplays");
        _windowDisplay = Load<WindowDisplay>(library, "SDL_GetWindowDisplayIndex");
        _countModes = Load<CountModes>(library, "SDL_GetNumDisplayModes");
        _getMode = Load<GetMode>(library, "SDL_GetDisplayMode");
        _currentMode = Load<CurrentMode>(library, "SDL_GetCurrentDisplayMode");
        _desktopMode = Load<CurrentMode>(library, "SDL_GetDesktopDisplayMode");
        _getBounds = Load<GetBounds>(library, "SDL_GetDisplayBounds");
        _setMode = Load<SetMode>(library, "SDL_SetWindowDisplayMode");
        _getError = Load<GetError>(library, "SDL_GetError");
        // The process owns this extra library reference; delegates remain valid through Game disposal.
    }

    public int Count => Math.Max(0, _count());

    public int MonitorFor(IntPtr window)
    {
        var index = _windowDisplay(window);
        Check(index);
        return index;
    }

    public Mode Current(int monitor)
    {
        Check(_currentMode(monitor, out var mode));
        return mode;
    }

    public Mode Desktop(int monitor)
    {
        Check(_desktopMode(monitor, out var mode));
        return mode;
    }

    public Rectangle Bounds(int monitor)
    {
        Check(_getBounds(monitor, out var bounds));
        return new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    public IReadOnlyList<Mode> Modes(int monitor)
    {
        var count = _countModes(monitor);
        Check(count);
        var modes = new List<Mode>(count);
        for (var index = 0; index < count; index++)
        {
            Check(_getMode(monitor, index, out var mode));
            modes.Add(mode);
        }

        return modes;
    }

    public void Select(IntPtr window, Mode mode) => Check(_setMode(window, ref mode));

    private void Check(int result)
    {
        if (result < 0)
        {
            throw new InvalidOperationException(Marshal.PtrToStringUTF8(_getError()) ?? "SDL display operation failed.");
        }
    }

    private static T Load<T>(IntPtr library, string symbol) where T : Delegate
    {
        return Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, symbol));
    }
}
