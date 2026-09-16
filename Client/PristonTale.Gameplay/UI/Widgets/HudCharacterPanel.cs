using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Widgets;

public sealed class HudCharacterPanel
{
    private static readonly string[] Attributes = ["Strength", "Spirit", "Talent", "Agility", "Health"];
    private OwnerState _state;
    private readonly string[] _values = new string[5];
    private string _level = "";
    private string _experience = "";
    private string _nextLevel = "";
    private string _points = "";
    private string _hp = "";
    private string _mp = "";
    private string _sp = "";
    private string _damage = "";
    private string _attackRating = "";
    private string _moveSpeed = "";
    private string _defence = "";
    private string _absorption = "";

    public string Name { get; set; } = "";
    public string ClassName { get; set; } = "";

    private static Rectangle Column(Rectangle panel, int index)
    {
        var width = (panel.Width - 48) / 3;
        return new Rectangle(panel.X + 16 + index * (width + 8), panel.Y + 44, width, 160);
    }

    public static Rectangle AllocateButton(Rectangle panel, int index)
    {
        var column = Column(panel, 1);
        return new Rectangle(column.Right - 30, column.Y + 28 + index * 26, 26, 24);
    }

    public void Draw(HudCanvas canvas, Rectangle panel, OwnerState state)
    {
        if (state is null)
        {
            return;
        }

        Refresh(state);
        var identity = Column(panel, 0);
        canvas.Text(Name, new Rectangle(identity.X + 8, identity.Y, identity.Width - 16, 28), scale: 0.95f);
        canvas.Text(ClassName, new Rectangle(identity.X + 8, identity.Y + 30, identity.Width - 16, 24), HudCanvas.Muted);
        canvas.Text(_level, new Rectangle(identity.X + 8, identity.Y + 60, identity.Width - 16, 24));
        canvas.Text(_experience, new Rectangle(identity.X + 8, identity.Y + 92, identity.Width - 16, 22), HudCanvas.Muted, 0.68f);
        canvas.Text(_nextLevel, new Rectangle(identity.X + 8, identity.Y + 118, identity.Width - 16, 22), HudCanvas.Muted, 0.68f);
        canvas.Bar(new Rectangle(identity.X + 8, identity.Y + 146, identity.Width - 16, 12),
            state.Experience - state.LevelStart, state.NextLevel - state.LevelStart, HudCanvas.Gold);

        var attributes = Column(panel, 1);
        canvas.Text(_points, new Rectangle(attributes.X + 8, attributes.Y, attributes.Width - 16, 22), scale: 0.7f);
        for (var index = 0; index < Attributes.Length; index++)
        {
            var row = new Rectangle(attributes.X, attributes.Y + 28 + index * 26, attributes.Width, 24);
            canvas.Frame(row, "Inset", 3);
            canvas.Text(Attributes[index], new Rectangle(row.X + 8, row.Y, row.Width - 102, row.Height), HudCanvas.Muted, 0.72f);
            canvas.Text(_values[index], new Rectangle(row.Right - 96, row.Y, 58, row.Height), scale: 0.75f, centered: true);
            if (state.StatPoints > 0)
            {
                var button = AllocateButton(panel, index);
                canvas.Frame(button, "Button", 4);
                canvas.Text("+", button, centered: true);
            }
        }

        var combat = Column(panel, 2);
        canvas.Bar(new Rectangle(combat.X + 4, combat.Y, combat.Width - 8, 20), state.Hp, state.MaxHp, new Color(188, 42, 45), _hp);
        canvas.Bar(new Rectangle(combat.X + 4, combat.Y + 22, combat.Width - 8, 20), state.Mp, state.MaxMp, new Color(60, 115, 203), _mp);
        canvas.Bar(new Rectangle(combat.X + 4, combat.Y + 44, combat.Width - 8, 20), state.Sp, state.MaxSp, new Color(72, 151, 103), _sp);
        canvas.Text(_damage, new Rectangle(combat.X + 10, combat.Y + 68, combat.Width - 20, 22), scale: 0.72f);
        canvas.Text(_attackRating, new Rectangle(combat.X + 10, combat.Y + 88, combat.Width - 20, 22), HudCanvas.Muted, 0.72f);
        canvas.Text(_moveSpeed, new Rectangle(combat.X + 10, combat.Y + 148, combat.Width - 20, 22), HudCanvas.Muted, 0.72f);
        canvas.Text(_defence, new Rectangle(combat.X + 10, combat.Y + 108, combat.Width - 20, 22), HudCanvas.Muted, 0.72f);
        canvas.Text(_absorption, new Rectangle(combat.X + 10, combat.Y + 128, combat.Width - 20, 22), HudCanvas.Muted, 0.72f);
    }

    private void Refresh(OwnerState state)
    {
        if (ReferenceEquals(state, _state))
        {
            return;
        }

        _state = state;
        _values[0] = state.Strength.ToString();
        _values[1] = state.Spirit.ToString();
        _values[2] = state.Talent.ToString();
        _values[3] = state.Agility.ToString();
        _values[4] = state.Health.ToString();
        _level = $"Level {state.Level}";
        _experience = $"Experience  {state.Experience:N0}";
        _nextLevel = state.NextLevel > state.LevelStart ? $"Next level  {Math.Max(0, state.NextLevel - state.Experience):N0}" : "Maximum level";
        _points = $"ATTRIBUTES    Points: {state.StatPoints}";
        _hp = $"HP  {state.Hp:N0} / {state.MaxHp:N0}";
        _mp = $"MP  {state.Mp:N0} / {state.MaxMp:N0}";
        _sp = $"SP  {state.Sp:N0} / {state.MaxSp:N0}";
        _attackRating = $"Attack rating  {state.AttackRating}";
        _moveSpeed = $"Move speed  {state.MoveSpeed}";
        _damage = $"Damage  {state.DamageMin} - {state.DamageMax}";
        _defence = $"Defence  {state.Defence}";
        _absorption = $"Absorption  {state.Absorption}";
    }
}
