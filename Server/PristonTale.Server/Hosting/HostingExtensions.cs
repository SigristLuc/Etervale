using PristonTale.Application.Chat;
using PristonTale.Application.GameMasters;
using PristonTale.Application.Services;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.GameMasters;
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
        builder.Services.AddSingleton<QuestAppService>();

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
            CrystalEvent = builder.Configuration.GetValue("World:CrystalEvent", 0),
            ItemEvents = new ItemEventRates
            {
                Mandoo = builder.Configuration.GetValue("World:ItemEvents:Mandoo", false),
                ValentineDay = builder.Configuration.GetValue("World:ItemEvents:ValentineDay", 0),
                Watermelon = builder.Configuration.GetValue("World:ItemEvents:Watermelon", 0),
                Valentine = builder.Configuration.GetValue("World:ItemEvents:Valentine", 0),
                CandyDays = builder.Configuration.GetValue("World:ItemEvents:CandyDays", 0),
                WhiteDay = builder.Configuration.GetValue("World:ItemEvents:WhiteDay", 0),
                Puzzle = builder.Configuration.GetValue("World:ItemEvents:Puzzle", 0),
                GiftBox = builder.Configuration.GetValue("World:ItemEvents:GiftBox", 0)
            },
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
        AddChat(builder);
        builder.Services.AddHostedService<PristonTcpServer>();
        builder.Services.AddHostedService<PristonTale.Server.World.QuestUpkeepService>();

        builder.Services.AddSingleton<IServerStatusReadModel, WorldStatusReadModel>();

        return builder;
    }

    // Chat and game masters: GameMasters:TestServer and GameMasters:DebugServer open the experience, gold and item-code
    // commands to every game master; GameMasters:PotionMonitor enables the potion report.
    private static void AddChat(HostApplicationBuilder builder)
    {
        var options = new GameMasterOptions(
            builder.Configuration.GetValue("GameMasters:TestServer", false),
            builder.Configuration.GetValue("GameMasters:DebugServer", false),
            builder.Configuration.GetValue("GameMasters:PotionMonitor", true),
            builder.Configuration.GetValue("GameMasters:ServerCode", 0));
        builder.Services.AddSingleton<ChatDirectory>();
        builder.Services.AddSingleton<GameServerControl>();
        builder.Services.AddSingleton(services =>
        {
            var logger = services.GetRequiredService<ILogger<GameMasterCommands>>();
            return new GameMasterCommands(services.GetRequiredService<ChatDirectory>(), services.GetRequiredService<WorldServer>(),
                services.GetRequiredService<WorldDataHolder>(), options, services.GetRequiredService<GameServerControl>(),
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
