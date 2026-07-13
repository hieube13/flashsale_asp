using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using FlashSale.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IOutboxEventRepository"/>.
/// TASK-015 producer writes a PENDING row inside the same transaction as the
/// <c>order_queue</c> insert; TASK-017 publisher (SELECT FOR UPDATE SKIP LOCKED)
/// reads and flips to PUBLISHED after Kafka ACK.
/// </summary>
public sealed class OutboxEventRepositoryImpl : IOutboxEventRepository
{
    private readonly FlashSaleDbContext _db;

    public OutboxEventRepositoryImpl(FlashSaleDbContext db) => _db = db;

    public async Task<OutboxEvent> AddAsync(OutboxEvent ev, CancellationToken ct = default)
    {
        _db.OutboxEvents.Add(ev);
        await _db.SaveChangesAsync(ct);
        return ev;
    }

    public async Task<IReadOnlyList<OutboxEvent>> FindPendingBatchAsync(int batchSize, CancellationToken ct = default)
        => await _db.OutboxEvents
            .AsNoTracking()
            .Where(e => e.Status == 0)
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task MarkPublishedAsync(long id, DateTime publishedAt, CancellationToken ct = default)
        => await _db.OutboxEvents
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Status, 1)
                .SetProperty(e => e.PublishedAt, publishedAt),
                ct);

    public async Task MarkPublishedBatchAsync(IReadOnlyList<long> ids, DateTime publishedAt, CancellationToken ct = default)
    {
        if (ids.Count == 0) return;
        await _db.OutboxEvents
            .Where(e => ids.Contains(e.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Status, 1)
                .SetProperty(e => e.PublishedAt, publishedAt),
                ct);
    }
}