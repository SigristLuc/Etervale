using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Widgets;

public enum HudMenuButton
{
    Status,
    Inventory,
    Skills,
    Party,
    Quest,
    System
}

public enum HudToggle
{
    Walk,
    Camera,
    Map
}

// The original interface bar: the bottom-anchored bar, gauges and buttons in 800x600 design coordinates.
public sealed class HudBar
{
    public const int LifeLength = 94;
    public const int ManaLength = 94;
    public const int StaminaLength = 76;
    public const int ExperienceLength = 219;
    public const int TimeLength = 50;

    private static readonly int[] MenuButtonX = [648, 673, 698, 723, 748, 772];
    private OwnerState _state;
    private string _life = "";
    private string _mana = "";
    private string _stamina = "";
    private string _experience = "";

    // Button cells: 25x27; the System cell starts at 772, so the Quest cell ends there (first match wins in the source).
    public static Rectangle MenuButton(HudMenuButton button)
    {
        var index = (int)button;
        var width = index + 1 < MenuButtonX.Length ? MenuButtonX[index + 1] - MenuButtonX[index] : 25;
        return new Rectangle(MenuButtonX[index], 560, width, 27);
    }
    public static Rectangle Toggle(HudToggle toggle) => new(569 + 26 * (int)toggle, 555, 26, 26);
    public static Rectangle Socket(bool right) => right ? new Rectangle(403, 541, 47, 46) : new Rectangle(349, 541, 49, 46);
    public static Rectangle LifeHover => new(316, 501, 21, 93);
    public static Rectangle ManaHover => new(464, 499, 19, 96);
    public static Rectangle StaminaHover => new(301, 514, 12, 81);
    public static Rectangle ExperienceHover => new(574, 592, 222, 7);

    // Opaque parts of Menu-1 and Menu-2 that block world clicks.
    public static readonly Rectangle[] Body = [new Rectangle(296, 490, 248, 110), new Rectangle(544, 552, 256, 48)];

    public static bool Captures(Point design)
    {
        foreach (var body in Body)
        {
            if (body.Contains(design))
            {
                return true;
            }
        }

        return false;
    }

    // Gauge length = full length * now / max, clamped to the image height.
    public static int Length(int full, long value, long maximum)
    {
        if (maximum <= 0 || value <= 0)
        {
            return 0;
        }

        return (int)Math.Min(full, full * value / maximum);
    }

    // The bitmap is filled from the bottom by cropping its top rows; returns the visible source rows.
    public static Rectangle GaugeSource(int width, int full, int length)
    {
        length = Math.Clamp(length, 0, full);
        return new Rectangle(0, full - length, width, length);
    }

    // Clock gauge: day 4h..23h fills left to right, night 23h..4h fills from the right.
    public static (bool Day, int Length) TimeGauge(float hour)
    {
        hour = float.IsFinite(hour) ? (hour % 24 + 24) % 24 : 0;
        var whole = MathF.Floor(hour);
        var day = whole is >= 4 and < 23;
        var length = day ? (int)(50f * ((whole - 4f) / 19f)) : (int)(50f * (((whole >= 23 ? whole - 24 : whole) + 1f) / 5f));
        return (day, Math.Clamp(length, 0, TimeLength));
    }

    public static float ExperienceFraction(OwnerState state)
    {
        if (state is null || state.NextLevel <= state.LevelStart)
        {
            return 0;
        }

        return (float)Math.Clamp((double)(state.Experience - state.LevelStart) / (state.NextLevel - state.LevelStart), 0, 1);
    }

    public void Draw(HudPainter painter, HudLayout layout, OwnerState state, HudBarView view)
    {
        var y = layout.BottomOffset;
        painter.Sprite(HudArt.Menu1, 288, 472 + y);
        painter.Sprite(HudArt.Menu2, 544, 536 + y);
        DrawGrow(painter, layout);
        painter.Sprite(HudArt.PotionBack, 495, 565 + y);
        if (state is not null)
        {
            var life = Length(LifeLength, state.Hp, state.MaxHp);
            var mana = Length(ManaLength, state.Mp, state.MaxMp);
            var stamina = Length(StaminaLength, state.Sp, state.MaxSp);
            painter.Sprite(HudArt.Life, 319, 500 + y + LifeLength - life, GaugeSource(16, LifeLength, life));
            painter.Sprite(HudArt.Mana, 465, 500 + y + ManaLength - mana, GaugeSource(16, ManaLength, mana));
            painter.Sprite(HudArt.Stamina, 303, 518 + y + StaminaLength - stamina, GaugeSource(8, StaminaLength, stamina));

            // The shipped Bar_Exp.bmp is a 221x5 horizontal strip (newer than the source's vertical 6x86 bar);
            // it sits in the Menu-2 slot whose dark interior starts at (575,593). Column 0 is its dark border.
            var experience = (int)(ExperienceLength * ExperienceFraction(state));
            if (experience > 0)
            {
                painter.Sprite(HudArt.Experience, 575, 593 + y, new Rectangle(1, 0, experience, 5));
            }
        }

        var (day, time) = TimeGauge(view.TimeOfDay);
        if (day)
        {
            painter.Sprite(HudArt.Sun, 363, 589 + y);
            painter.Sprite(HudArt.Time, 375, 593 + y, new Rectangle(0, 0, time, 5));
        }
        else
        {
            painter.Sprite(HudArt.Moon, 426, 589 + y);
            painter.Sprite(HudArt.Time, 375 + TimeLength - time, 593 + y, new Rectangle(TimeLength - time, 0, time, 5));
        }

        DrawSocket(painter, Socket(false), y, view.LeftSkill);
        DrawSocket(painter, Socket(true), y, view.RightSkill);

        if (!view.Running)
        {
            painter.Sprite(HudArt.Walk, 575, 565 + y);
        }

        if (view.MapVisible)
        {
            painter.Sprite(HudArt.MapOn, 575 + 48, 565 + y);
        }

        DrawButtons(painter, layout, view);
    }

    public void DrawHoverText(HudPainter painter, HudLayout layout, OwnerState state, Point pointer)
    {
        if (state is null)
        {
            return;
        }

        if (!ReferenceEquals(state, _state))
        {
            _state = state;
            _life = $"HP : {state.Hp}/{state.MaxHp}";
            _mana = $"MP : {state.Mp}/{state.MaxMp}";
            _stamina = $"STM : {state.Sp}/{state.MaxSp}";
            _experience = $"EXP: {ExperienceFraction(state) * 100:0.00}%";
        }

        var design = new Point(pointer.X, pointer.Y - layout.BottomOffset);
        var (text, position) = LifeHover.Contains(design) ? (_life, new Point(343, 500))
            : ManaHover.Contains(design) ? (_mana, new Point(490, 498))
            : StaminaHover.Contains(design) ? (_stamina, new Point(320, 513))
            : ExperienceHover.Contains(design) ? (_experience, new Point(572 + (224 - painter.Measure(_experience)) / 2, 571))
            : ("", Point.Zero);
        if (text.Length == 0)
        {
            return;
        }

        // As in the original, the item-info centre tile is stretched behind the text as a readability backdrop.
        var x = Math.Min(position.X, layout.Width - painter.Measure(text) - 6);
        painter.Stretch(HudArt.InfoBox[4], new Rectangle(x - 5, position.Y - 1 + layout.BottomOffset, painter.Measure(text) + 10, 15));
        painter.Text(text, x, position.Y + layout.BottomOffset);
    }

    private static void DrawSocket(HudPainter painter, Rectangle socket, int y, bool basicAttack)
    {
        // The original draws the skill box background (BOX4040.bmp) in an empty socket, but this asset version ships it as a solid
        // RGB(30,30,30) block and moves the socket background into Menu-1, so an empty socket shows the frame instead.
        if (basicAttack)
        {
            painter.Sprite(HudArt.SkillNormal, socket.X, socket.Y + y);
        }
    }

    private static void DrawGrow(HudPainter painter, HudLayout layout)
    {
        if (layout.Width <= HudLayout.DesignWidth)
        {
            return;
        }

        var top = layout.Height - 64;
        painter.Sprite(HudArt.GrowLeft, 800, top);
        var x = 866;
        while (x + 40 < layout.Width)
        {
            painter.Sprite(HudArt.GrowMiddle, x, top);
            x += 64;
        }

        painter.Sprite(HudArt.GrowRight, x, top);
    }

    private static void DrawButtons(HudPainter painter, HudLayout layout, HudBarView view)
    {
        var y = layout.BottomOffset;
        switch (view.HoveredToggle)
        {
            case HudToggle.Walk:
                painter.Sprite(view.Running ? HudArt.RunInfo : HudArt.WalkInfo, 575 + 12 - 38, 536 + y);
                break;
            case HudToggle.Camera:
                painter.Sprite(HudArt.CameraHandInfo, 575 + 24 + 12 - 38, 536 + y);
                break;
            case HudToggle.Map:
                painter.Sprite(view.MapVisible ? HudArt.MapOffInfo : HudArt.MapOnInfo, 575 + 48 + 12 - 38, 536 + y);
                break;
        }

        if (view.HoveredButton is not { } button)
        {
            return;
        }

        var bounds = MenuButton(button);
        var index = (int)button;
        if (view.ButtonDown)
        {
            painter.Sprite(HudArt.MenuButtonsDown[index], bounds.X, bounds.Y + y);
            return;
        }

        painter.Sprite(HudArt.MenuButtons[index], bounds.X, bounds.Y + y);
        painter.Sprite(HudArt.MenuInfo[index], 648 - (77 - 23 - 1) + index * 25, 533 + y);
    }
}

public readonly record struct HudBarView(
    float TimeOfDay,
    bool Running,
    bool MapVisible,
    bool LeftSkill,
    bool RightSkill,
    HudMenuButton? HoveredButton,
    bool ButtonDown,
    HudToggle? HoveredToggle);
