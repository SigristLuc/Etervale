namespace PristonTale.Simulation.World.Npcs;

public sealed record NpcInteraction(bool Ok, uint EntityId, string Name, NpcServices Services, IReadOnlyList<string> Dialog, string Message)
{
    public const float Reach = 96f;
    public static NpcInteraction Rejected(string message) => new(false, 0, "", NpcServices.None, Array.Empty<string>(), message);
}
