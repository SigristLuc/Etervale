using PristonTale.Application.World;

namespace PristonTale.Server.Hosting;

public sealed class WorldDataLoadService(
    WorldDataAppService worldData,
    WorldDataHolder holder,
    ILogger<WorldDataLoadService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var data = await worldData.BuildAsync(cancellationToken);
        holder.Current = data;
        logger.LogInformation(
            "World catalogs loaded: {Items} items / {Monsters} monsters / {Npcs} npcs / {Fields} fields-with-spawns.",
            data.Items.All.Count, data.Monsters.All.Count, data.Npcs.All.Count, data.FieldSpawns.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
