using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Migrations;
using PristonTale.Persistence.Repositories;

namespace PristonTale.Persistence.Sqlite;

public static class SqlitePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPristonSqlite(this IServiceCollection services, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        SqliteTypeMapping.EnsureRegistered();

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
        }.ToString();

        services.AddSingleton<IDbConnectionFactory>(new SqliteConnectionFactory(databasePath));
        services.AddSingleton<IAccountRepository, DapperAccountRepository>();
        services.AddSingleton<ICharacterRepository, DapperCharacterRepository>();
        services.AddSingleton<IWorldDataRepository, DapperWorldDataRepository>();
        services.AddSingleton<IDatabaseMigrator, FluentMigratorDatabaseMigrator>();

        services.AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(M013Baseline).Assembly).For.Migrations());

        return services;
    }
}
