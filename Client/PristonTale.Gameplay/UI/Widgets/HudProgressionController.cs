using PristonTale.Engine3D.Diagnostics;
using PristonTale.Simulation.Progression;

namespace PristonTale.Gameplay.UI.Widgets;

public sealed class HudProgressionController(Func<ProgressionCommand, Task<ProgressionOutcome>> apply, IClientLog log)
{
    private readonly HudRequest<ProgressionOutcome> _request = new(log, "Character progression request failed");
    public bool Pending => _request.Pending;
    public string Message { get; private set; } = "";

    public bool LastOk { get; private set; }
    public int Completions { get; private set; }

    public void Submit(ProgressionCommand command)
    {
        if (Pending || apply is null)
        {
            return;
        }

        Message = "Saving...";
        _request.Start(() => apply(command),
            _ => new ProgressionOutcome(false, command.ExpectedRevision, "Connection error. Check your character state before retrying."));
    }

    public void Update()
    {
        if (!_request.TryTake(out var outcome))
        {
            return;
        }

        Message = outcome.Ok ? "Saved" : outcome.Message;
        LastOk = outcome.Ok;
        Completions++;
    }
}
