using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering;

// Where a window without a usable saved position goes (the platform side is the client's NativeWindowPlacement).
public static class WindowPlacement
{
    // The top-left corner that centres a window's visible frame on a monitor's work area (the screen less the taskbar).
    // A frame taller than the work area keeps its title bar at the top edge, where it can still be dragged.
    public static Point Centered(Rectangle frame, Rectangle workArea)
    {
        var x = workArea.X + (workArea.Width - frame.Width) / 2;
        var y = workArea.Y + Math.Max(0, (workArea.Height - frame.Height) / 2);
        return new Point(x, y);
    }
}
