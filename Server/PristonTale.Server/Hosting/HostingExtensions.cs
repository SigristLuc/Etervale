using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Server.Net;
using PristonTale.Server.World;
using PristonTale.Persistence.Postgres;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Characters;

namespace PristonTale.Server.Hosting;

public static class HostingExtensions
{
    public const int DefaultGamePort = 10009;

    public static HostApplicationBuilder AddPristonServer(this HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ISessionRegistry, SessionRegistry>();
        builder.Services.AddSingleton<CharacterClassRegistry>();

        builder.Services.AddSingleton<AuthAppService>();
        builder.Services.AddSingleton<CharacterAppService>();
        // A factory prevents DI from selecting the collection constructor with an empty field set.
        builder.Services.AddSingleton(_ => new PristonTale.Simulation.World.WorldCatalog());
        builder.Services.AddSingleton<SessionAppService>();

        var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
        if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var databasePath = builder.Configuration["Database:SqlitePath"] ?? Path.Combine("data", "pristontale.db");
            builder.Services.AddPristonSqlite(SqliteDatabasePath.Resolve(databasePath));
        }
        else if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = builder.Configuration.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required for the Postgres provider.");
            builder.Services.AddPristonPostgres(connectionString);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported database provider '{provider}'. Use Sqlite or Postgres.");
        }
        builder.Services.AddHostedService<DatabaseMigrationService>();

        builder.Services.AddSingleton<WorldDataAppService>();
        builder.Services.AddSingleton<WorldDataHolder>();
        builder.Services.AddHostedService<WorldDataLoadService>();
        builder.Services.AddSingleton<FieldTerrainCatalog>();
        builder.Services.AddHostedService<FieldTerrainLoadService>();

        var worldOptions = new WorldServerOptions
        {
            ExperienceMultiplier = builder.Configuration.GetValue("World:ExperienceMultiplier", WorldServerOptions.DefaultExperienceMultiplier),
            MaxPendingCommandsPerPlayer = builder.Configuration.GetValue("World:MaxPendingCommandsPerPlayer",
                WorldServerOptions.DefaultMaxPendingCommandsPerPlayer)
        }.Validated();
        builder.Services.AddSingleton(services => new WorldServer(
            services.GetRequiredService<WorldDataHolder>(),
            services.GetRequiredService<PristonTale.Simulation.World.WorldCatalog>(),
            terrain: services.GetRequiredService<FieldTerrainCatalog>(),
            options: worldOptions));
        builder.Services.AddHostedService<WorldTickService>();
        builder.Services.AddHostedService<PristonTcpServer>();

        builder.Services.AddSingleton<IServerStatusReadModel, WorldStatusReadModel>();

        return builder;
    }
}
