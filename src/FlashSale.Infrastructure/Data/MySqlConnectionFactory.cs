using System.Data.Common;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace FlashSale.Infrastructure.Data;

public sealed class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(IOptions<MySqlOptions> options)
    {
        _connectionString = options.Value.ConnectionString
            ?? throw new InvalidOperationException("MySql:ConnectionString not configured");
    }

    public string ConnectionString => _connectionString;

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}

public sealed class MySqlOptions
{
    public string? ConnectionString { get; set; }
}