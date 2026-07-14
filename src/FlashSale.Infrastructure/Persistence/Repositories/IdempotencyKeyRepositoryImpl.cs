using Dapper;
using FlashSale.Domain.Repositories;
using FlashSale.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Persistence.Repositories;

/// <summary>
/// MySQL <c>INSERT IGNORE</c> backed implementation of
/// <see cref="IIdempotencyKeyRepository"/>. Mirrors Java
/// <c>IdempotencyKeyRepositoryImpl.tryInsert</c> (lines 17-20 of Java
/// IdempotencyKeyRepositoryImpl.java).
/// <para>
/// Why Dapper raw SQL rather than EF Core:
/// Pomelo's EF Core provider does NOT translate <c>INSERT IGNORE</c> —
/// it expects a 1:1 round-trip to <c>INSERT …</c> and would throw on the
/// duplicate-key violation. Dapper's <c>ExecuteAsync</c> with raw SQL lets
/// us stay in one round-trip and rely on MySQL's <c>affected_rows</c>
/// semantics: 1 = new, 0 = duplicate.
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
        const string sql = @"
INSERT IGNORE INTO idempotency_key (Token, CreatedAt, ExpiresAt)
VALUES (@Token, @CreatedAt, @ExpiresAt)";
        var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Token = token,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
        }, cancellationToken: ct));
        var isNew = rows == 1;
        _log.LogDebug("IdempotencyKey.TryInsert token={Token} new={IsNew} rows={Rows}", token, isNew, rows);
        return isNew;
    }
}