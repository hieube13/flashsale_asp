using Dapper;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using FlashSale.Domain.Services;
using FlashSale.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core + Dapper implementation of <see cref="IPaymentRepository"/> — TASK-018.
///
/// Static table writes (<c>payment_transaction</c>) go through EF Core. The order
/// amount lookup crosses a SHARD boundary — Java reads from
/// <c>ticket_order_{yyyyMM}</c> derived by parsing the trailing timestamp in the
/// order number, so we use Dapper with the shard table name resolved by
/// <see cref="IOrderDeductionDomainService.ExtractYearMonth"/>.
///
/// This is the only repository in the slice that uses BOTH EF and Dapper; the
/// pattern is intentional — see TASK-012 for the precedent on TickerOrder
/// (dynamic shards) vs static entities.
/// </summary>
public sealed class PaymentRepositoryImpl : IPaymentRepository
{
    private readonly FlashSaleDbContext _db;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IOrderDeductionDomainService _orderDomain;

    public PaymentRepositoryImpl(
        FlashSaleDbContext db,
        IDbConnectionFactory connectionFactory,
        IOrderDeductionDomainService orderDomain)
    {
        _db = db;
        _connectionFactory = connectionFactory;
        _orderDomain = orderDomain;
    }

    public async Task<PaymentTransaction> CreateAsync(PaymentTransaction tx, CancellationToken ct = default)
    {
        _db.PaymentTransactions.Add(tx);
        await _db.SaveChangesAsync(ct);
        return tx;
    }

    public async Task UpdateInProgressAsync(string paymentId, string paymentUrl, CancellationToken ct = default)
    {
        await _db.PaymentTransactions
            .Where(p => p.PaymentId == paymentId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.PaymentUrl, paymentUrl)
                .SetProperty(p => p.PaymentStatus, 1)         // IN_PROGRESS
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow),
                ct);
    }

    public async Task<PaymentTransaction?> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default)
        => await _db.PaymentTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId, ct);

    public Task UpdateStatusAsync(string paymentId, int status, string? gatewayTxId, CancellationToken ct = default)
    {
        var utc = DateTime.UtcNow;
        if (gatewayTxId is not null)
        {
            return _db.PaymentTransactions
                .Where(p => p.PaymentId == paymentId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.PaymentStatus, status)
                    .SetProperty(p => p.GatewayTransactionId, gatewayTxId)
                    .SetProperty(p => p.UpdatedAt, utc), ct);
        }
        else
        {
            return _db.PaymentTransactions
                .Where(p => p.PaymentId == paymentId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.PaymentStatus, status)
                    .SetProperty(p => p.UpdatedAt, utc), ct);
        }
    }

    public async Task<IReadOnlyList<PaymentTransaction>> FindByOrderNumberAsync(string orderNumber, CancellationToken ct = default)
        => await _db.PaymentTransactions
            .AsNoTracking()
            .Where(p => p.OrderNumber == orderNumber)
            .OrderByDescending(p => p.Id)
            .ToListAsync(ct);

    public async Task<decimal> GetOrderAmountAsync(string orderNumber, CancellationToken ct = default)
    {
        var yearMonth = _orderDomain.ExtractYearMonth(orderNumber);
        var shard = $"ticket_order_{yearMonth}";

        // Dapper-style raw scalar. Mirrors Java PaymentServiceImpl line 35-37.
        const string sql = "SELECT total_amount FROM `{0}` WHERE order_number = @orderNumber LIMIT 1";

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var raw = await conn.QueryFirstOrDefaultAsync<long?>(
            string.Format(sql, shard),
            new { orderNumber });

        // Java treats null as 0 — VNPay rejects with "Invalid amount" downstream.
        // Preserved here. Same conversion: stored total_amount is BIGINT VND.
        return raw is null ? 0m : raw.Value;
    }
}
