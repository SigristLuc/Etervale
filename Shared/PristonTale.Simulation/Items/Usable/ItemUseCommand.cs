namespace PristonTale.Simulation.Items.Usable;

// One use of an owned item, built from the inventory revision the client saw. Argument names a travel destination;
// Text is the line a firecracker shows.
public sealed record ItemUseCommand(Guid OperationId, long ExpectedRevision, Guid ItemId, int Argument = 0, string Text = "");

// Wire values of the use result; append only.
public enum ItemUseStatus : ushort
{
    Used = 0,
    NotUsable = 1,
    Stale = 2,
    Busy = 3,
    AlreadyRunning = 4,
    SameAttribute = 5,
    NotHere = 6,
    LevelTooLow = 7,
    NothingToChange = 8,
    Rejected = 9,
    StorageFailed = 10,
    Dead = 11,
    Delay = 12,
    Conflict = 13
}

public sealed record ItemUseOutcome(bool Ok, long Revision, ItemUseStatus Status, uint ItemCode, string Message)
{
    public static ItemUseOutcome Refused(long revision, ItemUseStatus status, uint itemCode = 0) =>
        new(false, revision, status, itemCode, ItemUseMessages.For(status));
}

public sealed record AppliedItemUse(ItemUseCommand Command, long Revision, ItemUseOutcome Outcome);

// The last item uses of a character, kept to answer a retried operation id with its first outcome.
public sealed class ItemUseHistory
{
    public const int Capacity = 64;

    private readonly List<AppliedItemUse> _applied = new();

    public IReadOnlyList<AppliedItemUse> Applied => _applied;

    public AppliedItemUse Find(Guid operationId) => _applied.FirstOrDefault(applied => applied.Command.OperationId == operationId);

    public void Record(ItemUseCommand command, ItemUseOutcome outcome)
    {
        if (_applied.Count == Capacity)
        {
            _applied.RemoveAt(0);
        }

        _applied.Add(new AppliedItemUse(command, outcome.Revision, outcome));
    }

    public void Restore(IEnumerable<AppliedItemUse> applied)
    {
        _applied.Clear();
        foreach (var entry in applied)
        {
            if (_applied.Count >= Capacity || entry?.Command is null || entry.Outcome is null || entry.Command.OperationId == Guid.Empty
                || _applied.Any(existing => existing.Command.OperationId == entry.Command.OperationId))
            {
                throw new ArgumentException("Invalid item use history.");
            }

            _applied.Add(entry);
        }
    }

    public void CopyFrom(ItemUseHistory other)
    {
        _applied.Clear();
        _applied.AddRange(other._applied);
    }
}
