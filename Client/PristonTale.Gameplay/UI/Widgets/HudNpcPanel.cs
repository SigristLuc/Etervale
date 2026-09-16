using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Widgets;

public sealed class HudNpcPanel(
    Func<uint, Task<NpcInteraction>> interact,
    IClientLog log,
    Action<NpcInteraction> opened = null,
    Action closed = null)
{
    private Task<NpcInteraction> _pending;
    private string[] _dialog = [];
    private bool _dismissed;
    private int _firstLine;
    public NpcInteraction Current { get; private set; }
    public bool ReadyToOpen { get; private set; }

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

    public void Update(Microsoft.Xna.Framework.Graphics.SpriteFont font)
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
        var text = Current.Ok ? string.Join(" ", Current.Dialog) : Current.Message;
        _dialog = HudTextLayout.Wrap(font, text, 352, 0.72f);
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

    public void Scroll(Rectangle panel, int wheelDelta)
    {
        var visibleLines = Math.Max(1, (panel.Height - 194) / 24);
        _firstLine = Math.Clamp(_firstLine - Math.Sign(wheelDelta) * 3, 0, Math.Max(0, _dialog.Length - visibleLines));
    }

    public static Rectangle TrainingButton(Rectangle panel) => new(panel.X + 24, panel.Bottom - 76, panel.Width - 48, 42);
    public bool CanTrain => Current?.Ok == true && (Current.Services & NpcServices.SkillMaster) != 0;

    public void Draw(HudCanvas canvas, Rectangle panel)
    {
        if (Current is null)
        {
            return;
        }

        canvas.Text(Current.Name, new Rectangle(panel.X + 24, panel.Y + 58, panel.Width - 48, 32), scale: 0.9f);
        var y = panel.Y + 102;
        for (var index = _firstLine; index < _dialog.Length; index++)
        {
            if (y + 24 > panel.Bottom - 92)
            {
                break;
            }

            canvas.Text(_dialog[index], new Rectangle(panel.X + 24, y, panel.Width - 48, 24), Color.White, 0.72f);
            y += 24;
        }

        if (CanTrain)
        {
            canvas.Frame(TrainingButton(panel), "Button", 8);
            canvas.Text("Skill training", TrainingButton(panel), centered: true);
        }
    }
}
