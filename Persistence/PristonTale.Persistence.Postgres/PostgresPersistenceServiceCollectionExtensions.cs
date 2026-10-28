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
    public static IServiceCollection AddPristonPostgres(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

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
