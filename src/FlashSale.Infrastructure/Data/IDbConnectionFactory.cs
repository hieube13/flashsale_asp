using System.Data.Common;

namespace FlashSale.Infrastructure.Data;

/// <summary>
/// Factory for database connections — used by Dapper for dynamic monthly tables.
/// SQL Server implementation: SqlServerConnectionFactory (TASK-025).
/// </summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken ct = default);
    string ConnectionString { get; }
}