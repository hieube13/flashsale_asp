using System.Data.Common;
using Dapper;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using FlashSale.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Data.Dynamic;

/// <summary>
/// Dapper-backed repository for monthly shard tables
/// <c>ticket_order_{yyyyMM}</c>. Mirrors Java's native-query repo
/// inside OrderDeductionDomainServiceImpl — table name is built at runtime
/// from the <c>yearMonth</c> argument supplied by the application service.
/// Reads return raw <c>object[]</c> rows (12-column projection in the exact
/// order declared by the shard DDL) so the application mapper stays in lock
/// step with Java's TicketOrderAppServiceImpl.&lt;method&gt; row projections.
/// </summary>
public sealed class TickerOrderRepositoryImpl : ITickerOrderRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<TickerOrderRepositoryImpl> _log;

    public TickerOrderRepositoryImpl(IDbConnectionFactory factory, ILogger<TickerOrderRepositoryImpl> log)
    {
        _factory = factory;
        _log = log;
    }

    public async Task InsertAsync(string yearMonth, TickerOrder order, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var table = ResolveTableName(yearMonth);
        const string sql = @"
INSERT INTO {0} (user_id, ticket_id, quantity, order_status, order_number,
                 total_amount, terminal_id, order_date, order_notes, updated_at, created_at)
VALUES (@UserId, @TicketId, @Quantity, @OrderStatus, @OrderNumber,
        @TotalAmount, @TerminalId, @OrderDate, @OrderNotes, @UpdatedAt, @CreatedAt)";
        var formatted = sql.Replace("{0}", table);
        var rows = await conn.ExecuteAsync(new CommandDefinition(formatted, new
        {
            order.UserId,
            order.TicketId,
            order.Quantity,
            order.OrderStatus,
            order.OrderNumber,
            TotalAmount = (long)order.TotalAmount,
            order.TerminalId,
            order.OrderDate,
            order.OrderNotes,
            order.UpdatedAt,
            order.CreatedAt,
        }, cancellationToken: ct));
        _log.LogInformation("Inserted order {OrderNumber} into {Table} ({Rows} row)", order.OrderNumber, table, rows);
    }

    public async Task<object?[]?> FindByOrderNumberAsync(string yearMonth, string orderNumber, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var table = ResolveTableName(yearMonth);
        var sql = $"SELECT id, user_id, ticket_id, quantity, order_status, order_number, " +
                  $"total_amount, terminal_id, order_date, order_notes, updated_at, created_at " +
                  $"FROM {table} WHERE order_number = @OrderNumber LIMIT 1";
        var row = await conn.QueryFirstOrDefaultAsync(
            new CommandDefinition(sql, new { OrderNumber = orderNumber }, cancellationToken: ct));
        return row is null ? null : ((IDictionary<string, object?>)row).Values.ToArray();
    }

    public async Task<IReadOnlyList<object?[]>> FindAllAsync(string yearMonth, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var table = ResolveTableName(yearMonth);
        var sql = $"SELECT id, user_id, ticket_id, quantity, order_status, order_number, " +
                  $"total_amount, terminal_id, order_date, order_notes, updated_at, created_at " +
                  $"FROM {table} ORDER BY id DESC LIMIT 1000";
        var rows = await conn.QueryAsync(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(r => ((IDictionary<string, object?>)r).Values.ToArray()).ToList();
    }

    public async Task<IReadOnlyList<object?[]>> FindPageAsync(string yearMonth, long lastId, int limit, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var table = ResolveTableName(yearMonth);
        var sql = $"SELECT id, user_id, ticket_id, quantity, order_status, order_number, " +
                  $"total_amount, terminal_id, order_date, order_notes, updated_at, created_at " +
                  $"FROM {table} WHERE id < @LastId ORDER BY id DESC LIMIT @Limit";
        var rows = await conn.QueryAsync(new CommandDefinition(sql,
            new { LastId = lastId, Limit = limit }, cancellationToken: ct));
        return rows.Select(r => ((IDictionary<string, object?>)r).Values.ToArray()).ToList();
    }

    public async Task<bool> UpdateStatusAsync(string yearMonth, string orderNumber, int status, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var table = ResolveTableName(yearMonth);
        var sql = $"UPDATE {table} SET order_status = @Status, updated_at = @UpdatedAt WHERE order_number = @OrderNumber";
        var rows = await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Status = status, OrderNumber = orderNumber, UpdatedAt = DateTime.UtcNow },
            cancellationToken: ct));
        return rows > 0;
    }

    private static string ResolveTableName(string yearMonth)
    {
        if (string.IsNullOrWhiteSpace(yearMonth) || yearMonth.Length != 6 ||
            !yearMonth.All(char.IsDigit))
            throw new ArgumentException($"yearMonth must be 6 digits yyyyMM (got '{yearMonth}')", nameof(yearMonth));
        return $"ticket_order_{yearMonth}";
    }
}
