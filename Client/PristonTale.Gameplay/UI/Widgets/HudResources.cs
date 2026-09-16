using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Widgets;

public sealed class HudResources
{
    private OwnerState _state;
    private string _hp = "";
    private string _mp = "";
    private string _sp = "";
    private string _progress = "";
    private string _identity = "";
    public string CharacterName { get; set; } = "";

    public void Draw(HudCanvas canvas, Rectangle bounds, OwnerState state)
    {
        var frame = At(bounds, 0, 4, 500, 86);
        canvas.Image("Resources", frame);
        if (state is null)
        {
            canvas.Text("Receiving character state...", bounds, HudCanvas.Muted, centered: true);
            return;
        }

        if (state != _state)
        {
            _state = state;
            _hp = $"Health: {state.Hp:N0} / {state.MaxHp:N0}";
            _mp = $"Mana: {state.Mp:N0} / {state.MaxMp:N0}";
            _sp = $"Stamina: {state.Sp:N0} / {state.MaxSp:N0}";
            _progress = $"Level {state.Level} | XP {state.Experience:N0} / {state.NextLevel:N0}";
            _identity = $"{CharacterName} | Level {state.Level} | Gold {state.Money:N0}";
        }

        canvas.Gauge("Health", frame, state.MaxHp > 0 ? (float)state.Hp / state.MaxHp : 0);
        canvas.Bar(At(bounds, 67, 40, 137, 21), state.Mp, state.MaxMp, new Color(54, 122, 211));
        canvas.Bar(At(bounds, 298, 40, 137, 21), state.Sp, state.MaxSp, new Color(81, 158, 107));
        canvas.Bar(At(bounds, 67, 95, 368, 7), state.Experience - state.LevelStart,
            state.NextLevel - state.LevelStart, new Color(214, 180, 87));
    }

    public string HoverText(Rectangle bounds, Point pointer)
    {
        if (_state is null)
        {
            return "";
        }

        if (At(bounds, 217, 10, 67, 78).Contains(pointer))
        {
            return _hp;
        }

        if (At(bounds, 67, 33, 137, 34).Contains(pointer))
        {
            return _mp;
        }

        if (At(bounds, 298, 33, 137, 34).Contains(pointer))
        {
            return _sp;
        }

        if (At(bounds, 67, 89, 368, 20).Contains(pointer))
        {
            return _progress;
        }

        return bounds.Contains(pointer) ? _identity : "";
    }

    private static Rectangle At(Rectangle bounds, int x, int y, int width, int height)
    {
        var scale = bounds.Width / 500f;
        return new Rectangle(bounds.X + (int)(x * scale), bounds.Y + (int)(y * scale), (int)(width * scale), (int)(height * scale));
    }
}
