using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Widgets;

// NPC dialog in the original help box (at 50,45, 9x10 tiles); skill masters also get the
// skill master box (ReStartMain.bmp) with the skill, class-change (inert) and exit buttons.
public sealed class HudNpcPanel(
    Func<uint, Task<NpcInteraction>> interact,
    IClientLog log,
    Action<NpcInteraction> opened = null,
    Action closed = null)
{
    public const int BoxX = 50;
    public const int BoxY = 45;
    public const int SizeW = 9;
    public const int SizeH = 10;
    public const int VisibleLines = 16;
    private const int TextWidth = SizeW * 32 + 64 - 64;

    private Task<NpcInteraction> _pending;
    private string[] _dialog = [];
    private bool _dismissed;
    private int _firstLine;
    public NpcInteraction Current { get; private set; }
    public bool ReadyToOpen { get; private set; }
    public bool CanTrain => Current?.Ok == true && (Current.Services & NpcServices.SkillMaster) != 0;

    public static Rectangle Box => HudPainter.HelpBoxBounds(BoxX, BoxY, SizeW, SizeH);
    public static Rectangle Title => new(BoxX + (SizeW * 32 + 64) / 2 - 76, BoxY + 14, 152, 28);
    public static Rectangle OkButton => new(BoxX + (SizeW * 32 + 64) / 2 - 24 + 9, BoxY + SizeH * 32 - 2 + 4, 32, 16);

    // SkillMasterMainXY is centred on the window; the original showed no help box beside it, so it moves right of the dialog.
    public static Point SkillMasterOrigin(HudLayout layout)
    {
        var origin = new Point(layout.Width / 2 - 93 - 30, layout.Height / 2 - 63 - 100);
        return new Rectangle(origin, new Point(188, 128)).Intersects(Box) ? new Point(Box.Right + 8, origin.Y) : origin;
    }
    public static Rectangle SkillMasterBox(HudLayout layout) => new(SkillMasterOrigin(layout), new Point(188, 128));
    public static Rectangle TrainingButton(HudLayout layout) => SkillMasterButton(layout, 0);
    public static Rectangle ExitButton(HudLayout layout) => SkillMasterButton(layout, 2);

    private static Rectangle SkillMasterButton(HudLayout layout, int index)
    {
        var origin = SkillMasterOrigin(layout);
        return new Rectangle(origin.X + 21 + 51 * index, origin.Y + 22, 44, 44);
    }

    public void Request(uint entityId)
    {
        if (_pending is null && interact is not null)
        {
            Current = null;
            _dismissed = false;
            _firstLine = 0;
            _pending = InteractAsync(entityId);
        }
    }

    private async Task<NpcInteraction> InteractAsync(uint entityId)
    {
        try
        {
            return await interact(entityId);
        }
        catch (Exception exception)
        {
            log.Error("NPC interaction failed", exception);
            return NpcInteraction.Rejected("Connection error. Please try again.");
        }
    }

    public void Update(SpriteFont font)
    {
        ReadyToOpen = false;
        if (_pending is null || !_pending.IsCompleted)
        {
            return;
        }

        Current = _pending.Result;
        _pending = null;
        if (_dismissed)
        {
            Current = null;
            return;
        }

        _dialog = Current.Ok
            ? Current.Dialog.SelectMany(line => HudTextLayout.Wrap(font, line, TextWidth, HudPainter.TextScale)).ToArray()
            : HudTextLayout.Wrap(font, Current.Message, TextWidth, HudPainter.TextScale);
        _firstLine = 0;
        ReadyToOpen = true;
        opened?.Invoke(Current);
    }

    public void Dismiss()
    {
        if (!_dismissed)
        {
            closed?.Invoke();
        }

        _dismissed = true;
        ReadyToOpen = false;
    }

    public void Scroll(int wheelDelta)
    {
        _firstLine = Math.Clamp(_firstLine - Math.Sign(wheelDelta) * 3, 0, Math.Max(0, _dialog.Length - VisibleLines));
    }

    public void Draw(HudPainter painter, HudLayout layout, Point pointer, bool showSkillMaster)
    {
        if (Current is null)
        {
            return;
        }

        painter.HelpBox(BoxX, BoxY, SizeW, SizeH);
        var title = Title;
        painter.Sprite(HudArt.HelpTitle, title.X, title.Y);
        painter.TextCentered(painter.Fit(Current.Name, title.Width - 20), title.Left, title.Right, title.Y + 7, new Color(255, 255, 180));
        for (var line = 0; line < VisibleLines && _firstLine + line < _dialog.Length; line++)
        {
            painter.Text(_dialog[_firstLine + line], BoxX + 30, BoxY + 66 + 15 * line);
        }

        if (_dialog.Length > VisibleLines)
        {
            var barX = BoxX + (SizeW - 1) * 32 + 80 - 6;
            painter.Stretch(HudArt.ScrollBar, new Rectangle(barX, BoxY + 53, 8, 256));
            var travel = 236 * _firstLine / Math.Max(1, _dialog.Length - VisibleLines);
            painter.Sprite(HudArt.ScrollButton, barX - 4, BoxY + 53 + 7 + travel - 7);
        }

        var ok = OkButton;
        painter.Sprite(HudArt.HelpButton, ok.X - 9, ok.Y - 4);
        painter.Sprite(ok.Contains(pointer) ? HudArt.OkYellow : HudArt.OkGray, ok.X, ok.Y);
        if (showSkillMaster && CanTrain)
        {
            DrawSkillMaster(painter, layout, pointer);
        }
    }

    private static void DrawSkillMaster(HudPainter painter, HudLayout layout, Point pointer)
    {
        var origin = SkillMasterOrigin(layout);
        painter.Sprite(HudArt.SkillMasterBox, origin.X, origin.Y);
        var training = TrainingButton(layout);
        painter.Sprite(training.Contains(pointer) ? HudArt.SelectSkill : HudArt.SelectSkillGray, training.X, training.Y);
        var job = SkillMasterButton(layout, 1);
        painter.Sprite(HudArt.SelectJobGray, job.X, job.Y);
        var exit = ExitButton(layout);
        if (exit.Contains(pointer))
        {
            painter.Sprite(HudArt.SkillMasterExit, exit.X, exit.Y);
        }

        painter.Text("Skill training", origin.X + 23, origin.Y + 73);
    }
}
