using Dapper;
using FlashSale.Domain.Repositories;
using FlashSale.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQL Server <c>IF NOT EXISTS ... INSERT</c> backed implementation of
/// <see cref="IIdempotencyKeyRepository"/>. Mirrors Java
/// <c>IdempotencyKeyRepositoryImpl.tryInsert</c> (lines 17-20 of Java
/// IdempotencyKeyRepositoryImpl.java).
/// <para>
/// Why Dapper raw SQL rather than EF Core:
/// Dapper's <c>ExecuteAsync</c> with raw SQL lets us use
/// <c>IF NOT EXISTS ... INSERT</c> in one round-trip:
/// rows = 1 means new, rows = 0 means duplicate (T-SQL doesn't have
/// <c>INSERT IGNORE</c> like MySQL).
/// </para>
/// </summary>
public sealed class IdempotencyKeyRepositoryImpl : IIdempotencyKeyRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<IdempotencyKeyRepositoryImpl> _log;

    public IdempotencyKeyRepositoryImpl(IDbConnectionFactory factory, ILogger<IdempotencyKeyRepositoryImpl> log)
    {
        _factory = factory;
        _log = log;
    }

    public async Task<bool> TryInsertAsync(string token, DateTime expiresAt, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        // T-SQL: IF NOT EXISTS + INSERT returns rows from SELECT (1) + INSERT (1) = 2 when new,
        // or 1 from SELECT when existing. Use SET NOCOUNT + @@ROWCOUNT to get clean semantics.
        const string sql = @"
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM idempotency_key WHERE Token = @Token)
BEGIN
    INSERT INTO idempotency_key (Token, CreatedAt, ExpiresAt)
    VALUES (@Token, @CreatedAt, @ExpiresAt);
    SELECT @Token AS Token, 1 AS IsNew;
END
ELSE
BEGIN
    SELECT @Token AS Token, 0 AS IsNew;
END";
        var rows = await conn.QueryAsync<TokenInsertResult>(new CommandDefinition(sql, new
        {
            Token = token,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
        }, cancellationToken: ct));
        var result = rows.SingleOrDefault();
        var isNew = result?.IsNew == 1;
        _log.LogDebug("IdempotencyKey.TryInsert token={Token} new={IsNew}", token, isNew);
        return isNew;
    }

    private record TokenInsertResult(string Token, int IsNew);
}
