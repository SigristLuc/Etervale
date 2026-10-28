using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI.Parties;

public enum HudPlayerButton
{
    Trade,
    Party,
    Friend
}

// The buttons under another player's portrait once the local player reached it, as the original's trade, party and friend
// requests: cw-1.tga (100x50) from 50 px left of the portrait's centre and 6 px above its bottom, the 20x20 buttons from
// 36 px left of the centre at 25 px steps along the bottom, the hovered one's tip 60 px to its left. The portrait shows no
// life bar while they do.
public static class HudPlayerButtons
{
    private static readonly HudPlayerButton[] All = Enum.GetValues<HudPlayerButton>();

    public static Rectangle Menu(HudTargetFrame frame) => new(Center(frame) - 50, frame.Box.Bottom - 6, 100, 50);

    public static Rectangle Button(HudTargetFrame frame, HudPlayerButton button) =>
        new(Center(frame) - 36 + (int)button * 25, frame.Box.Bottom, 20, 20);

    public static HudPlayerButton? At(HudTargetFrame frame, Point pointer)
    {
        foreach (var button in All)
        {
            if (Button(frame, button).Contains(pointer))
            {
                return button;
            }
        }

        return null;
    }

    public static void Draw(HudPainter painter, HudTargetFrame frame, Point pointer)
    {
        painter.Stretch(HudPartyArt.PlayerMenu, Menu(frame));
        var hovered = At(frame, pointer);
        foreach (var button in All)
        {
            var bounds = Button(frame, button);
            var index = (int)button;
            painter.Sprite(button == hovered ? HudPartyArt.PlayerButtons[index] : HudPartyArt.PlayerButtonsIdle[index], bounds.X, bounds.Y);
        }

        if (hovered is { } tip)
        {
            var bounds = Button(frame, tip);
            painter.Sprite(HudPartyArt.PlayerTips[(int)tip], bounds.X - 60, bounds.Y + 1);
        }
    }

    private static int Center(HudTargetFrame frame) => frame.Box.X + (frame.Box.Width >> 1);
}
