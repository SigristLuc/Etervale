using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Npc;

// The original item box above the pointer: the name in the item colour and up to two lines below it.
public static class HudItemTooltip
{
    public static void Draw(HudPainter painter, HudLayout layout, Point pointer, string name, string first, string second = "")
    {
        var lines = new[] { first, second }.Where(line => !string.IsNullOrEmpty(line)).ToArray();
        var textWidth = Math.Max(painter.Measure(name), lines.Select(painter.Measure).DefaultIfEmpty(0).Max());
        var width = Math.Min(layout.Width - 8, textWidth + 24);
        var size = HudPainter.InfoBoxBounds(0, 0, width, 31 + lines.Length * 17);
        var bounds = layout.PlaceOverlay(new Point(pointer.X - size.Width / 2, pointer.Y - size.Height - 8), size.Width, size.Height);
        bounds = HudPainter.InfoBoxBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        painter.Flush();
        painter.InfoBox(bounds);
        painter.TextCentered(painter.Fit(name, bounds.Width - 16), bounds.Left, bounds.Right, bounds.Y + 9, new Color(255, 255, 180));
        for (var index = 0; index < lines.Length; index++)
        {
            painter.TextCentered(painter.Fit(lines[index], bounds.Width - 16), bounds.Left, bounds.Right, bounds.Y + 26 + 17 * index,
                new Color(200, 200, 200));
        }
    }
}
