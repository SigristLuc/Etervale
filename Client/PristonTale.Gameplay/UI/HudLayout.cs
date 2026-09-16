using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI;

public readonly record struct HudLayout(float Scale, Rectangle Resources, Rectangle Menu, Rectangle Panel, int CellSize)
{
    public int ViewportWidth { get; init; }
    public int ViewportHeight { get; init; }

    public static HudLayout Create(int width, int height, float requestedScale, bool docked = false)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        requestedScale = float.IsFinite(requestedScale) ? requestedScale : 1;
        var scale = Math.Clamp(requestedScale, 0.75f, 1.5f);
        scale = Math.Min(scale, Math.Min(width / 800f, height / 600f));
        var logicalWidth = (int)(width / scale);
        var logicalHeight = (int)(height / scale);
        var resources = new Rectangle((logicalWidth - 600) / 2, logicalHeight - 148, 600, 136);
        var menu = new Rectangle(logicalWidth - 190, logicalHeight - 82, 174, 66);
        if (menu.Intersects(resources))
        {
            menu.Y = resources.Y - 76;
        }

        var panel = new Rectangle(logicalWidth - 432, 28, 416, Math.Min(440, logicalHeight - 180));
        if (docked)
        {
            var panelWidth = Math.Min(960, logicalWidth - 16);
            panel = new Rectangle((logicalWidth - panelWidth) / 2, logicalHeight - 368, panelWidth, 360);
            resources.Y = panel.Bottom - 136;
            menu.X = panel.Right - 179;
            menu.Y = panel.Bottom - 74;
            resources.X = Math.Min(resources.X, menu.X - resources.Width - 1);
        }

        return new HudLayout(scale,
            resources,
            menu,
            panel,
            docked ? 28 : 32)
        {
            ViewportWidth = width,
            ViewportHeight = height
        };
    }

    public Point ToLogical(Point pointer) => new((int)(pointer.X / Scale), (int)(pointer.Y / Scale));

    public Rectangle PlaceOverlay(Point preferred, int width, int height)
    {
        var scale = float.IsFinite(Scale) && Scale > 0 ? Scale : 1;
        var viewportWidth = Math.Max(1, (int)(ViewportWidth / scale));
        var viewportHeight = Math.Max(1, (int)(ViewportHeight / scale));
        var margin = Math.Min(8, (Math.Min(viewportWidth, viewportHeight) - 1) / 2);
        width = Math.Clamp(width, 1, viewportWidth - margin * 2);
        height = Math.Clamp(height, 1, viewportHeight - margin * 2);
        var x = Math.Clamp(preferred.X, margin, viewportWidth - width - margin);
        var y = Math.Clamp(preferred.Y, margin, viewportHeight - height - margin);
        return new Rectangle(x, y, width, height);
    }
}
