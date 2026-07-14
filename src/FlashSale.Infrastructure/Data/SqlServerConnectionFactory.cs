using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace FlashSale.Infrastructure.Data;

/// <summary>
/// SQL Server connection factory — used by Dapper for dynamic monthly tables.
/// Replaces MySqlConnectionFactory (MySQL → SQL Server migration, TASK-025).
/// </summary>
public sealed class SqlServerConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlServerConnectionFactory(IOptions<SqlServerOptions> options)
    {
        _connectionString = options.Value.ConnectionString
            ?? throw new InvalidOperationException("SqlServer:ConnectionString not configured");
    }

    public string ConnectionString => _connectionString;

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}

public sealed class SqlServerOptions
{
    public string? ConnectionString { get; set; }
}
