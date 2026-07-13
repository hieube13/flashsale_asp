using FlashSale.Application.Services;
using FlashSale.Domain.Entities;
using FlashSale.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Persistence;

/// <summary>
/// EF Core-backed implementation of <see cref="IOrderMqTransactionService"/>.
/// Opens an <see cref="IDbContextTransaction"/>, attaches both entities to the
/// same DbContext, and commits. Any exception inside the using-block triggers
/// EF's automatic rollback when the transaction goes out of scope.
/// </summary>
public sealed class OrderMqTransactionServiceImpl : IOrderMqTransactionService
{
    private readonly FlashSaleDbContext _db;
    private readonly ILogger<OrderMqTransactionServiceImpl> _log;

    public OrderMqTransactionServiceImpl(FlashSaleDbContext db, ILogger<OrderMqTransactionServiceImpl> log)
    {
        _db = db;
        _log = log;
    }

    public async Task PersistAsync(OrderQueue queue, OutboxEvent outboxEvent, CancellationToken ct = default)
    {
        await using IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct);
        _db.OrderQueues.Add(queue);
        _db.OutboxEvents.Add(outboxEvent);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _log.LogDebug("outbox + order_queue committed for token={Token}", queue.Token);
    }
}