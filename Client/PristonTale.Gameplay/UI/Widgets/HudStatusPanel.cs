using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Widgets;

// Original status window: Status.bmp with its text columns and the attribute point arrows.
// The element resistances (RegiBox) are not part of the authoritative owner state and stay empty.
public sealed class HudStatusPanel
{
    private const int TextShift = 2;
    private static readonly int[] PointButtonY = [431, 452, 472, 491, 511];
    private static readonly (int Left, int Right, int Y)[] Values =
    [
        (75, 160, 442 + TextShift), (75, 160, 463 + TextShift), (75, 128, 503 + TextShift), (75, 160, 543 + TextShift), (75, 160, 563 + TextShift),
        (391, 482, 429 + TextShift), (391, 482, 454 + TextShift), (391, 482, 479 + TextShift),
        (585, 632, 433 + TextShift), (585, 632, 454 + TextShift), (585, 632, 473 + TextShift), (585, 632, 493 + TextShift), (585, 632, 513 + TextShift),
        (710, 769, 455 + TextShift - 1), (710, 769, 474 + TextShift - 1), (710, 769, 495 + TextShift - 1), (710, 769, 515 + TextShift - 1),
        (710, 769, 534 + TextShift - 1), (585, 632, 533 + TextShift - 1)
    ];

    private readonly string[] _text = new string[Values.Length];
    private OwnerState _state;

    public string Name { get; set; } = "";
    public string ClassName { get; set; } = "";

    public static Rectangle AllocateButton(Point origin, int index) => new(origin.X + 566, origin.Y + PointButtonY[index] + 1, 18, 17);
    public static Rectangle Close(Point origin) => new(origin.X + 251, origin.Y + 565, 20, 20);
    public static Rectangle Window(Point origin) => new(origin.X, origin.Y + 400, HudLayout.DesignWidth, 200);

    public void Draw(HudPainter painter, Point origin, OwnerState state, Point pointer, bool pressed, bool interactive)
    {
        painter.Sprite(HudArt.Status, origin.X, origin.Y + 400);
        if (state is null)
        {
            return;
        }

        Refresh(state);
        if (state.StatPoints > 0)
        {
            painter.Sprite(HudArt.StatusPoint, origin.X + 520, origin.Y + 531);
            for (var index = 0; index < PointButtonY.Length; index++)
            {
                var button = AllocateButton(origin, index);
                painter.Sprite(HudArt.StatusArrow, button.X, button.Y);
                if (interactive && pressed && button.Contains(pointer))
                {
                    painter.Sprite(HudArt.StatusArrowDown, button.X + 1, button.Y + 1);
                }
            }
        }

        for (var index = 0; index < Values.Length; index++)
        {
            var (left, right, y) = Values[index];
            painter.TextCentered(_text[index], origin.X + left, origin.X + right, origin.Y + y);
        }
    }

    private void Refresh(OwnerState state)
    {
        if (ReferenceEquals(state, _state))
        {
            return;
        }

        _state = state;
        _text[0] = ClassName;
        _text[1] = Name;
        _text[2] = state.Level.ToString();
        _text[3] = state.Experience.ToString();
        _text[4] = state.NextLevel.ToString();
        _text[5] = $"{state.Hp}/{state.MaxHp}";
        _text[6] = $"{state.Mp}/{state.MaxMp}";
        _text[7] = $"{state.Sp}/{state.MaxSp}";
        _text[8] = state.Strength.ToString();
        _text[9] = state.Spirit.ToString();
        _text[10] = state.Talent.ToString();
        _text[11] = state.Agility.ToString();
        _text[12] = state.Health.ToString();
        _text[13] = state.AttackRating.ToString();
        _text[14] = $"{Math.Max(0, state.DamageMin)}-{Math.Max(0, state.DamageMax)}";
        _text[15] = state.Defence.ToString();
        _text[16] = state.Absorption.ToString();
        _text[17] = state.MoveSpeed.ToString();
        _text[18] = state.StatPoints > 0 ? state.StatPoints.ToString() : "";
    }

    public void Invalidate() => _state = null;
}
