using PristonTale.Persistence.Abstractions;

namespace PristonTale.Server.Hosting;

public sealed class DatabaseMigrationService(IDatabaseMigrator migrator, ILogger<DatabaseMigrationService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await migrator.MigrateAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration failed — persistence-backed features will be unavailable.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
