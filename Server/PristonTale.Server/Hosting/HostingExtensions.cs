using PristonTale.Application.Chat;
using PristonTale.Application.GameMasters;
using PristonTale.Application.Services;
using PristonTale.Application.Settings;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Persistence.Abstractions.Settings;
using PristonTale.Persistence.Abstractions.Warehouses;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Server.Net;
using PristonTale.Server.World;
using PristonTale.Persistence.Postgres;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Loot;

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
        builder.Services.AddSingleton<NpcAppService>();
        builder.Services.AddSingleton<ItemUseAppService>();
        builder.Services.AddSingleton<RestartAppService>();
        builder.Services.AddSingleton<WingGateAppService>();
        builder.Services.AddSingleton<QuestAppService>();
        builder.Services.AddSingleton<PersonalShopAppService>();

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
            MaxPendingCommandsPerPlayer = builder.Configuration.GetValue("World:MaxPendingCommandsPerPlayer",
                WorldServerOptions.DefaultMaxPendingCommandsPerPlayer)
        }.Validated();
        builder.Services.AddSingleton(worldOptions);
        builder.Services.AddSingleton(services => new WorldServer(
            services.GetRequiredService<WorldDataHolder>(),
            services.GetRequiredService<PristonTale.Simulation.World.WorldCatalog>(),
            terrain: services.GetRequiredService<FieldTerrainCatalog>(),
            options: worldOptions));
        builder.Services.AddHostedService<WorldTickService>();
        AddChat(builder);
        builder.Services.AddSingleton(services =>
        {
            var gameMasters = services.GetRequiredService<GameMasterCommands>();
            var settings = new ServerSettingsAppService(services.GetRequiredService<IServerSettingsRepository>(),
                services.GetRequiredService<WorldServer>(), gameMasters, services.GetRequiredService<GameServerControl>(), worldOptions);
            gameMasters.Settings = settings;
            return settings;
        });
        // After the world tick (settings apply on it) and before the game server opens.
        builder.Services.AddHostedService<ServerSettingsService>();
        builder.Services.AddHostedService<GameTcpServer>();
        builder.Services.AddHostedService<PristonTale.Server.World.QuestUpkeepService>();

        builder.Services.AddSingleton<IServerStatusReadModel, WorldStatusReadModel>();

        return builder;
    }

    private static void AddChat(HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ChatDirectory>();
        builder.Services.AddSingleton<GameServerControl>();
        builder.Services.AddSingleton(services =>
        {
            var logger = services.GetRequiredService<ILogger<GameMasterCommands>>();
            return new GameMasterCommands(services.GetRequiredService<ChatDirectory>(), services.GetRequiredService<WorldServer>(),
                services.GetRequiredService<WorldDataHolder>(), GameMasterOptions.Default, services.GetRequiredService<GameServerControl>(),
                services.GetRequiredService<IGameMasterRepository>(), ex => logger.LogError(ex, "Could not write the game-master audit."))
            {
                Accounts = services.GetRequiredService<IAccountRepository>(),
                Characters = services.GetRequiredService<ICharacterRepository>(),
                Warehouses = services.GetRequiredService<IWarehouseRepository>()
            };
        });
        builder.Services.AddSingleton(services => new ChatAppService(services.GetRequiredService<ChatDirectory>(),
            services.GetRequiredService<WorldServer>(), services.GetRequiredService<GameMasterCommands>()));
    }
}
