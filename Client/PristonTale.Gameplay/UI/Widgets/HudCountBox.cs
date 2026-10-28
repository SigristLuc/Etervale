using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// The original's count box, the quantity question (items to drop, goods to sell or buy, money to move): two lines and a
// number typed with the digit keys (Backspace erases) or stepped with the arrows. Any number may be typed; one above the
// maximum gives the maximum when accepted (the arrows stay within it). OK or Enter accepts, Cancel or Escape
// (GameplayHud.ClosePanel) closes. It starts at zero; accepting zero does nothing.
public sealed class HudCountBox
{
    private const int MaxDigits = 9;
    private string _first = "";
    private string _second = "";
    private int _maximum;
    private Action<int> _accepted;
    private string _text = "0";

    public static Rectangle Bounds => HudMessageBox.Bounds;
    public static Rectangle Field => new(Bounds.X + 34, Bounds.Y + 62, 109, 25);
    public static Rectangle Up => new(Bounds.X + 124, Bounds.Y + 62, 19, 13);
    public static Rectangle Down => new(Bounds.X + 124, Bounds.Y + 75, 19, 13);
    public static Rectangle Ok => HudMessageBox.Accept;
    public static Rectangle Cancel => HudMessageBox.Reject;

    public bool IsOpen { get; private set; }
    public int Count { get; private set; }

    // maximum: as much as the question allows (gold may pass what the box can count; it stops there).
    public void Open(string first, string second, long maximum, Action<int> accepted)
    {
        (_first, _second, _maximum, _accepted) = (first, second, (int)Math.Clamp(maximum, 0, int.MaxValue), accepted);
        Show(0);
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _accepted = null;
    }

    // Typed characters and the keys pressed this frame (pressed: true only on the frame a key goes down).
    public void Type(ReadOnlySpan<char> typed, Func<Keys, bool> pressed)
    {
        foreach (var character in typed)
        {
            if (char.IsAsciiDigit(character) && _text.Length < MaxDigits)
            {
                Show(Count * 10 + (character - '0'));
            }
            else if (character == '\b')
            {
                Show(Count / 10);
            }
        }

        if (pressed(Keys.Enter))
        {
            Accept();
        }
    }

    public void Click(Point pointer)
    {
        if (Up.Contains(pointer))
        {
            Show(Math.Clamp(Count + 1, 0, _maximum));
        }
        else if (Down.Contains(pointer))
        {
            Show(Math.Clamp(Count - 1, 0, _maximum));
        }
        else if (Ok.Contains(pointer))
        {
            Accept();
        }
        else if (Cancel.Contains(pointer))
        {
            Close();
        }
    }

    public void Draw(HudPainter painter, Point pointer)
    {
        var bounds = Bounds;
        painter.Sprite(HudArt.MessageBox, bounds.X, bounds.Y);
        painter.TextCentered(painter.Fit(_first, HudMessageBox.VisibleWidth - 16), bounds.Left, bounds.Right, bounds.Y + 20, new Color(255, 255, 180));
        painter.TextCentered(painter.Fit(_second, HudMessageBox.VisibleWidth - 16), bounds.Left, bounds.Right, bounds.Y + 40);
        painter.Sprite(HudArt.CountField, Field.X, Field.Y);
        painter.Text(_text, Field.X + 8, Field.Y + 6);
        if (Up.Contains(pointer))
        {
            painter.Sprite(HudArt.CountUp, Up.X, Up.Y);
        }
        else if (Down.Contains(pointer))
        {
            painter.Sprite(HudArt.CountDown, Down.X, Down.Y);
        }

        painter.Sprite(Ok.Contains(pointer) ? HudArt.OkYellow : HudArt.OkGray, Ok.X, Ok.Y);
        painter.Sprite(Cancel.Contains(pointer) ? HudArt.CancelYellow : HudArt.CancelGray, Cancel.X, Cancel.Y);
    }

    private void Show(int count)
    {
        Count = count;
        _text = count.ToString();
    }

    private void Accept()
    {
        var accepted = _accepted;
        var count = Math.Min(Count, _maximum);
        Close();
        if (count > 0)
        {
            accepted?.Invoke(count);
        }
    }
}
