using System.Data.Common;
using Microsoft.Data.Sqlite;
using PristonTale.Persistence.Abstractions;

namespace PristonTale.Persistence.Sqlite;

public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        // Single chokepoint that guarantees the SQLite Dapper config (bool<->INTEGER, underscores) is
        // applied before any query runs — even when repos are constructed directly without the DI extension.
        SqliteTypeMapping.EnsureRegistered();
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
        }.ToString();
    }

    public async Task<DbConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
