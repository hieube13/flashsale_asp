using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using FlashSale.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IOrderQueueRepository"/>.
/// Single-table CRUD on <c>order_queue</c> — used by TASK-015 producer
/// (insert PENDING row inside the transactional outbox) and TASK-016
/// consumer (read by token, flip status, write orderNumber).
/// </summary>
public sealed class OrderQueueRepositoryImpl : IOrderQueueRepository
{
    private readonly FlashSaleDbContext _db;

    public OrderQueueRepositoryImpl(FlashSaleDbContext db) => _db = db;

    public async Task<OrderQueue> AddAsync(OrderQueue queue, CancellationToken ct = default)
    {
        _db.OrderQueues.Add(queue);
        await _db.SaveChangesAsync(ct);
        return queue;
    }

    public async Task<OrderQueue?> GetByTokenAsync(string token, CancellationToken ct = default)
        => await _db.OrderQueues.AsNoTracking().FirstOrDefaultAsync(q => q.Token == token, ct);

    public async Task UpdateStatusAsync(string token, int status, string? orderNumber, string? message, CancellationToken ct = default)
    {
        // .NET 8 / Pomelo: tracked update is one round-trip; we avoid loading
        // the entity first because OrderQueue has a small fixed column set.
        var rows = await _db.OrderQueues
            .Where(q => q.Token == token)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(q => q.Status, status)
                .SetProperty(q => q.OrderNumber, orderNumber)
                .SetProperty(q => q.Message, message)
                .SetProperty(q => q.UpdatedAt, DateTime.UtcNow),
                ct);
        if (rows == 0)
            throw new InvalidOperationException($"order_queue row not found for token={token}");
    }
}