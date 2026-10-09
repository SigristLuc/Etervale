using PristonTale.Engine3D.Diagnostics;
using PristonTale.Simulation.Progression;

namespace PristonTale.Gameplay.UI.Widgets;

public sealed class HudProgressionController(Func<ProgressionCommand, Task<ProgressionOutcome>> apply, IClientLog log)
{
    private Task<ProgressionOutcome> _pending;
    public bool Pending => _pending is not null;
    public string Message { get; private set; } = "";
    public int Completions { get; private set; }

    public void Submit(ProgressionCommand command)
    {
        if (Pending || apply is null)
        {
            return;
        }

        Message = "Saving...";
        _pending = ApplyAsync(command);
    }

    private async Task<ProgressionOutcome> ApplyAsync(ProgressionCommand command)
    {
        try
        {
            return await apply(command);
        }
        catch (Exception exception)
        {
            log.Error("Character progression request failed", exception);
            return new ProgressionOutcome(false, command.ExpectedRevision, "Connection error. Check your character state before retrying.");
        }
    }

    public void Update()
    {
        if (_pending is null || !_pending.IsCompleted)
        {
            return;
        }

        var outcome = _pending.Result;
        Message = outcome.Ok ? "Saved" : outcome.Message;
        _pending = null;
        Completions++;
    }
}
