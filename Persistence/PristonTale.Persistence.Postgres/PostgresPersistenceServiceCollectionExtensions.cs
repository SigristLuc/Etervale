using Dapper;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Contacts;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Persistence.Abstractions.Settings;
using PristonTale.Persistence.Abstractions.Warehouses;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Migrations;
using PristonTale.Persistence.Repositories;

namespace PristonTale.Persistence.Postgres;

public static class PostgresPersistenceServiceCollectionExtensions
{
    // Below PostgreSQL's default max_connections (100, three reserved), so a full pool never meets "too many clients".
    public const int DefaultMaxPoolSize = 20;

    // maxPoolSize replaces the connection string's Maximum Pool Size.
    public static IServiceCollection AddPristonPostgres(this IServiceCollection services, string connectionString, int maxPoolSize = DefaultMaxPoolSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPoolSize, 1);
        connectionString = new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = maxPoolSize }.ConnectionString;

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        SqlMapper.AddTypeHandler(new JsonTextNpgsqlHandler());

        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddSingleton<IAccountRepository, DapperAccountRepository>();
        services.AddSingleton<ICharacterRepository, DapperCharacterRepository>();
        services.AddSingleton<IWorldDataRepository, DapperWorldDataRepository>();
        services.AddSingleton<IWarehouseRepository, DapperWarehouseRepository>();
        services.AddSingleton<IContactRepository, DapperContactRepository>();
        services.AddSingleton<IGameMasterRepository, DapperGameMasterRepository>();
        services.AddSingleton<IServerSettingsRepository, DapperServerSettingsRepository>();
        services.AddSingleton<IDatabaseMigrator, FluentMigratorDatabaseMigrator>();

        services.AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(M013Baseline).Assembly).For.Migrations());

        return services;
    }
}
