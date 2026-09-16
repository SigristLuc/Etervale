using Microsoft.AspNetCore.Server.Kestrel.Core;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Server.Net;
using PristonTale.Server.World;
using PristonTale.Persistence.Postgres;
using PristonTale.Simulation.Characters;

namespace PristonTale.Server.Hosting;

public static class HostingExtensions
{
    public const int DefaultGamePort = 10012;

    public static WebApplicationBuilder AddPristonServer(this WebApplicationBuilder builder)
    {
        builder.Services.AddGrpc();
        builder.Services.AddSingleton<ISessionRegistry, SessionRegistry>();
        builder.Services.AddSingleton<CharacterClassRegistry>();

        builder.Services.AddSingleton<AuthAppService>();
        builder.Services.AddSingleton<CharacterAppService>();
        // A factory prevents DI from selecting the collection constructor with an empty field set.
        builder.Services.AddSingleton(_ => new PristonTale.Simulation.World.WorldCatalog());
        builder.Services.AddSingleton<SessionAppService>();

        var connectionString = builder.Configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5433;Database=pristontale;Username=pristontale;Password=pristontale";
        builder.Services.AddPristonPostgres(connectionString);
        builder.Services.AddHostedService<DatabaseMigrationService>();

        builder.Services.AddSingleton<WorldDataAppService>();
        builder.Services.AddSingleton<WorldDataHolder>();
        builder.Services.AddHostedService<WorldDataLoadService>();

        builder.Services.AddSingleton<WorldServer>();
        builder.Services.AddHostedService<WorldTickService>();

        builder.Services.AddSingleton<IServerStatusReadModel, WorldStatusReadModel>();

        builder.WebHost.ConfigureKestrel(static options =>
            options.ListenAnyIP(DefaultGamePort, static listen => listen.Protocols = HttpProtocols.Http2));

        return builder;
    }

    public static WebApplication MapPristonServices(this WebApplication app)
    {
        app.MapGrpcService<AuthGrpcService>();
        app.MapGrpcService<CharacterGrpcService>();
        app.MapGrpcService<GameSessionGrpcService>();
        app.MapGet("/", static () => $"PristonTale game server. gRPC (h2c) on :{DefaultGamePort}.");
        return app;
    }
}
