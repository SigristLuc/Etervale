using System.Collections.Concurrent;
using System.Diagnostics;
using PristonTale.Application.World;
using PristonTale.Geometry;
using PristonTale.Simulation.World;

namespace PristonTale.Server.Hosting;

// Loads every field's stage collision at startup, as the original server does in srReadStage (OnSever.cpp).
// A missing assets directory or stage leaves that field without terrain checks instead of stopping the server.
public sealed class FieldTerrainLoadService(
    WorldCatalog world,
    FieldTerrainCatalog terrain,
    IConfiguration configuration,
    ILogger<FieldTerrainLoadService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var root = configuration["Assets:Root"] is { Length: > 0 } configured ? configured : AssetPath.FindAssetsRoot();
        if (root is null || !Directory.Exists(root))
        {
            logger.LogWarning("No assets directory found (set Assets:Root); fields run without terrain collision.");
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        var loaded = new ConcurrentDictionary<int, FieldTerrain>();
        var triangles = 0L;
        await Parallel.ForEachAsync(world.AllFields, cancellationToken, async (field, ct) =>
        {
            var path = string.IsNullOrEmpty(field.StageSmd) ? null : AssetPath.Resolve(root, field.StageSmd);
            if (path is null)
            {
                logger.LogWarning("Field {Field} ({Code}): stage {Stage} not found.", field.Id, field.FieldCode, field.StageSmd);
                return;
            }

            try
            {
                var mesh = StageCollisionReader.Read(await File.ReadAllBytesAsync(path, ct));
                loaded[field.FieldCode] = new FieldTerrain(mesh);
                Interlocked.Add(ref triangles, mesh.CollisionTriangleCount);
            }
            catch (InvalidDataException ex)
            {
                logger.LogWarning("Field {Field} ({Code}): stage {Stage} unreadable: {Message}", field.Id, field.FieldCode, field.StageSmd, ex.Message);
            }
        });

        terrain.Publish(new Dictionary<int, FieldTerrain>(loaded));
        var memory = (GC.GetTotalMemory(forceFullCollection: true) - memoryBefore) / (1024.0 * 1024.0);
        logger.LogInformation("Field terrain loaded: {Loaded}/{Total} stages, {Triangles} collision triangles, {Memory:F0} MiB in {Elapsed:F0} ms.",
            loaded.Count, world.AllFields.Count, triangles, memory, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
