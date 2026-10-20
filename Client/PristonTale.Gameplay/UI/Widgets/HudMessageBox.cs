using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// Original two-button message box: BoxMain.tga at (300,200), top anchored, with two 32x16 buttons and two text lines.
public static class HudMessageBox
{
    private const int VisibleWidth = 168;

    public static Rectangle Bounds => new(300, 200, VisibleWidth, 124);
    public static Rectangle Accept => new(300 + 40, 200 + 95, 32, 16);
    public static Rectangle Reject => new(300 + 94, 200 + 95, 32, 16);

    public static void Draw(HudPainter painter, string first, string second, Point pointer)
    {
        var bounds = Bounds;
        painter.Sprite(HudArt.MessageBox, bounds.X, bounds.Y);
        painter.Sprite(Accept.Contains(pointer) ? HudArt.YesYellow : HudArt.YesGray, Accept.X, Accept.Y);
        painter.Sprite(Reject.Contains(pointer) ? HudArt.NoYellow : HudArt.NoGray, Reject.X, Reject.Y);
        painter.TextCentered(painter.Fit(first, VisibleWidth - 16), bounds.Left, bounds.Right, bounds.Y + 20, new Color(255, 255, 180));
        painter.TextCentered(painter.Fit(second, VisibleWidth - 16), bounds.Left, bounds.Right, bounds.Y + 43);
    }
}
