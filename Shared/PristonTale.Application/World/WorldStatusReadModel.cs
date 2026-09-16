namespace PristonTale.Application.World;

public sealed class WorldStatusReadModel(WorldServer world) : IServerStatusReadModel
{
    public ServerStatus GetStatus()
    {
        var fields = world.ActiveFieldCounts()
            .Select(static f => new FieldStatus(f.FieldId, f.PlayerCount))
            .ToList();
        return new ServerStatus(world.PlayerCount, fields);
    }
}
