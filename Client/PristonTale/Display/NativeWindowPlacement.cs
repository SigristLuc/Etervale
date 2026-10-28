using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Display;

// The window's position on Windows. SDL creates the window at MonoGame's default 800x480, centred, and the back buffer
// then resizes it keeping its top-left corner, so a large saved size spilled off the bottom-right of the screen. The
// first windowed apply restores the position and maximized state saved on exit (when its title bar is still on a
// monitor) or centres the window on the work area; later applies only bring a window that left the work area back.
// MonoGame's Native platform exposes neither the work area nor the frame, hence Win32; other platforms keep SDL's
// placement. Positions are the restored frame's top-left in Windows' workspace coordinates (Get/SetWindowPlacement).
internal static class NativeWindowPlacement
{
    private const int SwShowNormal = 1;
    private const int SwShowMinimized = 2;
    private const int SwShowMaximized = 3;
    private const int WpfRestoreToMaximized = 2;
    private const int MonitorDefaultToNull = 0;
    private const int MonitorDefaultToNearest = 2;
    private const int DwmwaExtendedFrameBounds = 9;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    // The strip of a saved frame that has to land on a monitor: the title bar, by which the window is dragged.
    private const int TitleBand = 32;

    private static IntPtr _window;

    public static bool TryRead(out int x, out int y, out bool maximized)
    {
        x = y = 0;
        maximized = false;
        if (!TryGetWindow(out var window) || !TryGetPlacement(window, out var placement))
        {
            return false;
        }

        x = placement.NormalPosition.Left;
        y = placement.NormalPosition.Top;
        maximized = placement.ShowCommand == SwShowMaximized
            || placement.ShowCommand == SwShowMinimized && (placement.Flags & WpfRestoreToMaximized) != 0;
        return true;
    }

    public static bool IsMaximized() => TryGetWindow(out var window) && IsZoomed(window);

    public static void Restore(ClientSettings settings)
    {
        if (!TryGetWindow(out var window) || !TryGetPlacement(window, out var placement))
        {
            return;
        }

        if (settings.WindowX is int x && settings.WindowY is int y)
        {
            var normal = placement.NormalPosition;
            var frame = new Rect(x, y, x + normal.Right - normal.Left, y + normal.Bottom - normal.Top);
            var title = frame with { Bottom = frame.Top + TitleBand };
            if (MonitorFromRect(ref title, MonitorDefaultToNull) != IntPtr.Zero)
            {
                placement.NormalPosition = frame;
                placement.ShowCommand = settings.WindowMaximized ? SwShowMaximized : SwShowNormal;
                placement.Flags = 0;
                SetWindowPlacement(window, ref placement);
                return;
            }
        }

        Center(window);
        if (settings.WindowMaximized)
        {
            ShowWindow(window, SwShowMaximized);
        }
    }

    public static void KeepOnScreen()
    {
        if (!TryGetWindow(out var window) || IsZoomed(window) || IsIconic(window)
            || !TryGetFrame(window, out _, out var visible) || !TryGetWorkArea(window, out var work))
        {
            return;
        }

        if (!work.Contains(visible))
        {
            Center(window);
        }
    }

    private static void Center(IntPtr window)
    {
        if (!TryGetFrame(window, out var outer, out var visible) || !TryGetWorkArea(window, out var work))
        {
            return;
        }

        // The outer rectangle includes the invisible resize borders; the visible frame is what gets centred.
        var target = WindowPlacement.Centered(visible, work);
        SetWindowPos(window, IntPtr.Zero, outer.Left + target.X - visible.X, outer.Top + target.Y - visible.Y, 0, 0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private static bool TryGetFrame(IntPtr window, out Rect outer, out Rectangle visible)
    {
        visible = Rectangle.Empty;
        if (!GetWindowRect(window, out outer))
        {
            return false;
        }

        var frame = outer;
        if (DwmGetWindowAttribute(window, DwmwaExtendedFrameBounds, out var extended, Marshal.SizeOf<Rect>()) == 0)
        {
            frame = extended;
        }

        visible = frame.ToRectangle();
        return true;
    }

    private static bool TryGetWorkArea(IntPtr window, out Rectangle work)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var found = GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref info);
        work = found ? info.Work.ToRectangle() : Rectangle.Empty;
        return found;
    }

    private static bool TryGetPlacement(IntPtr window, out WindowPlacementData placement)
    {
        placement = new WindowPlacementData { Length = Marshal.SizeOf<WindowPlacementData>() };
        return GetWindowPlacement(window, ref placement);
    }

    // The SDL window of the game thread, which created it and applies the display.
    private static bool TryGetWindow(out IntPtr window)
    {
        if (_window == IntPtr.Zero && OperatingSystem.IsWindows())
        {
            var className = new StringBuilder(16);
            EnumThreadWindows(GetCurrentThreadId(), (candidate, _) =>
            {
                className.Clear();
                if (GetClassName(candidate, className, className.Capacity) > 0 && className.ToString() == "SDL_app")
                {
                    _window = candidate;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
        }

        window = _window;
        return window != IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private record struct Rect(int Left, int Top, int Right, int Bottom)
    {
        public readonly Rectangle ToRectangle() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacementData
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public NativePoint MinPosition;
        public NativePoint MaxPosition;
        public Rect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacementData placement);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPlacement(IntPtr window, ref WindowPlacementData placement);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref Rect rect, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);
}
