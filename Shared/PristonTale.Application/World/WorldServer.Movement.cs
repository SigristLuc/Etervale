using System.Numerics;

namespace PristonTale.Application.World;

// Receives the verdict of a movement report on the tick thread. It must return at once: no IO, no locks held elsewhere.
public interface IMovementVerdictSink
{
    void OnMovementVerdict(MovementVerdict verdict);
}

// Movement reports are the bulk of the command traffic (every moving player, five times a second). They travel through
// the same ordered queue as every other command, but as plain values: no closure, task or completion per report.
public sealed partial class WorldServer
{
    // Queued like any command of the player (same quota, same order); false when the quota or the queue is full, and the
    // caller drops the report like a lost packet (the next one carries a newer position).
    public bool TryReportMovement(int fieldId, uint entityId, Vector3 position, float facing, bool moving, bool running, uint actionTarget,
        IMovementVerdictSink? sink)
    {
        var pending = _pendingByPlayer.GetOrAdd(entityId, static _ => new PendingCommands());
        if (Interlocked.Increment(ref pending.Count) > _options.MaxPendingCommandsPerPlayer)
        {
            Interlocked.Decrement(ref pending.Count);
            return false;
        }

        var report = new MovementCommand(fieldId, entityId, position, facing, moving, running, actionTarget, sink, pending);
        if (_commands.Writer.TryWrite(new WorldCommand(null, report)))
        {
            return true;
        }

        Interlocked.Decrement(ref pending.Count);
        return false;
    }

    private void ApplyMovement(in MovementCommand report)
    {
        Interlocked.Decrement(ref report.Pending.Count);
        var verdict = MovementVerdict.Ignored;
        if (_connections.TryGetValue(report.EntityId, out var connection) && connection.FieldId == report.FieldId
            && _fields.TryGetValue(report.FieldId, out var field))
        {
            verdict = connection.Player.ReportMovement(report.Position, report.Facing, report.Moving, report.Running, report.ActionTarget, field.NowMs,
                field.Terrain);
        }

        report.Sink?.OnMovementVerdict(verdict);
    }

    private readonly record struct MovementCommand(
        int FieldId,
        uint EntityId,
        Vector3 Position,
        float Facing,
        bool Moving,
        bool Running,
        uint ActionTarget,
        IMovementVerdictSink? Sink,
        PendingCommands Pending);

    // One queued command: a closure, or a movement report carried by value.
    private readonly record struct WorldCommand(Action? Action, MovementCommand Movement);
}
