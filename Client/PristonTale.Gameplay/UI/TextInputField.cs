using System.Text;
using Microsoft.Xna.Framework.Input;

namespace PristonTale.Gameplay.UI;

public sealed class TextInputField
{
    private readonly StringBuilder _text = new();
    private double _caretSeconds;

    public bool Focused { get; set; }
    public bool Masked { get; init; }
    public int MaxLength { get; init; } = 20;

    public string Text => _text.ToString();
    public string DisplayText => Masked ? new string('*', _text.Length) : _text.ToString();
    public bool CaretVisible => Focused && _caretSeconds % 1.0 < 0.5;

    public void Clear() => _text.Clear();

    public void Update(in KeyboardState current, in KeyboardState previous, double dtSeconds)
    {
        _caretSeconds += dtSeconds;
        if (!Focused)
            return;

        var shift = current.IsKeyDown(Keys.LeftShift) || current.IsKeyDown(Keys.RightShift);
        foreach (var key in current.GetPressedKeys())
        {
            if (previous.IsKeyDown(key))
                continue;

            if (key == Keys.Back)
            {
                if (_text.Length > 0)
                    _text.Remove(_text.Length - 1, 1);
            }
            else if (_text.Length < MaxLength && TryMapKey(key, shift, out var ch))
            {
                _text.Append(ch);
            }
        }
    }

    private static bool TryMapKey(Keys key, bool shift, out char ch)
    {
        ch = '\0';
        if (key >= Keys.A && key <= Keys.Z)
        {
            var lower = (char)('a' + (key - Keys.A));
            ch = shift ? char.ToUpperInvariant(lower) : lower;
            return true;
        }
        if (key >= Keys.D0 && key <= Keys.D9 && !shift)
        {
            ch = (char)('0' + (key - Keys.D0));
            return true;
        }
        if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
        {
            ch = (char)('0' + (key - Keys.NumPad0));
            return true;
        }
        switch (key)
        {
            case Keys.OemPeriod: ch = '.'; return true;
            case Keys.OemMinus: ch = shift ? '_' : '-'; return true;
            default: return false;
        }
    }
}
