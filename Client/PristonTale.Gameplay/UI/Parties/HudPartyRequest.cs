using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Parties;

namespace PristonTale.Gameplay.UI.Parties;

// The original's party request box at (513, 3): MessageBackGround.tga with who asks at (36, 16) and the request line at
// (28, 38) (naming the player a member recommends to its leader), a blue square blinking over them (shown frames 16..30
// of every 31 at 70 per second), OK at (140, 11) and cancel at (144, 37). It closes with its answer or after
// PartyRules.InviteMs.
public sealed class HudPartyRequest
{
    private const int FramesPerSecond = 70;
    private static readonly Color Blink = Color.FromNonPremultiplied(0, 200, 255, 80);

    private double _openedMs;
    private string _line = PartyMessages.Request;

    public static readonly Rectangle Box = new(513, 3, 256, 64);
    public static readonly Rectangle Ok = new(Box.X + 140, Box.Y + 11, 23, 23);
    public static readonly Rectangle Cancel = new(Box.X + 144, Box.Y + 37, 15, 15);

    public PartyRequestInfo Current { get; private set; }
    public bool IsOpen => Current is not null;

    public void Show(PartyRequestInfo request, double nowMs)
    {
        Current = request;
        _openedMs = nowMs;
        _line = request.Recommended.Length > 0 ? PartyMessages.Format(PartyMessages.Recommends, request.Recommended) : PartyMessages.Request;
    }

    public void Close() => Current = null;

    public void Update(double nowMs)
    {
        if (Current is not null && nowMs - _openedMs >= PartyRules.InviteMs)
        {
            Close();
        }
    }

    public bool Captures(Point pointer) => IsOpen && (Ok.Contains(pointer) || Cancel.Contains(pointer));

    // The answer a click gives (true accept, false refuse), or null when it missed both buttons.
    public bool? Click(Point pointer)
    {
        if (!IsOpen)
        {
            return null;
        }

        var answer = Ok.Contains(pointer) ? true : Cancel.Contains(pointer) ? false : (bool?)null;
        if (answer is not null)
        {
            Close();
        }

        return answer;
    }

    public void Draw(HudPainter painter, Point pointer, double nowMs)
    {
        if (Current is not { } request)
        {
            return;
        }

        painter.Sprite(HudPartyArt.RequestBox, Box.X, Box.Y);
        if ((long)((nowMs - _openedMs) * FramesPerSecond / 1000) % 31 > 15)
        {
            painter.Fill(new Rectangle(Box.X + 5, Box.Y + 5, 160, 53), Blink);
        }

        painter.Text(request.FromName, Box.X + 36, Box.Y + 16);
        painter.Text(_line, Box.X + 28, Box.Y + 38);
        painter.Sprite(Ok.Contains(pointer) ? HudPartyArt.OkHovered : HudPartyArt.Ok, Ok.X, Ok.Y);
        painter.Sprite(Cancel.Contains(pointer) ? HudPartyArt.CancelHovered : HudPartyArt.Cancel, Cancel.X, Cancel.Y);
    }
}
