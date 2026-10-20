using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

public enum HudSystemEntry
{
    Settings,
    CharacterSelect,
    SubServers,
    Help,
    Exit
}

// Original exit button menu. The shipped ExitMenu.tga is a newer 128x159 five-entry sheet
// (the original draws a 128x64 three-entry menu at 698,504), so it is raised to end just above the menu buttons.
public static class HudSystemMenu
{
    public const int X = 698;
    public const int Y = 560 - 159;

    private static readonly string[] Hover = [HudArt.SystemSettings, null, null, HudArt.SystemHelp, HudArt.SystemExit];

    public static Rectangle Bounds => new(X + 9, Y + 5, 80, 148);
    public static Rectangle Entry(HudSystemEntry entry) => new(X + 9, Y + 5 + 30 * (int)entry, 80, 18);

    public static HudSystemEntry? At(Point design)
    {
        for (var index = 0; index < Hover.Length; index++)
        {
            if (Entry((HudSystemEntry)index).Contains(design))
            {
                return (HudSystemEntry)index;
            }
        }

        return null;
    }

    public static void Draw(HudPainter painter, HudLayout layout, Point pointer)
    {
        var offset = layout.BottomOffset;
        painter.Sprite(HudArt.SystemMenu, X, Y + offset);
        if (At(new Point(pointer.X, pointer.Y - offset)) is { } entry && Hover[(int)entry] is { } hover)
        {
            var bounds = Entry(entry);
            painter.Sprite(hover, X, bounds.Y + offset);
        }
    }
}
