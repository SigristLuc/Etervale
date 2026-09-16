namespace PristonTale.Application.World;

public sealed record ServerStatus(int PlayersOnline, IReadOnlyList<FieldStatus> ActiveFields)
{
    public static readonly ServerStatus Empty = new(0, []);
}

public sealed record FieldStatus(int FieldCode, int PlayerCount);
