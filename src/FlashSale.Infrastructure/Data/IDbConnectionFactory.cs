using System.Data.Common;

namespace FlashSale.Infrastructure.Data;

/// <summary>
/// Factory for MySQL connections — used by Dapper for dynamic monthly tables.
/// </summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken ct = default);
    string ConnectionString { get; }
}