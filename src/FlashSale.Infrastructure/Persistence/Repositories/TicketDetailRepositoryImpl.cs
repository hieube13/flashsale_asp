using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using FlashSale.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ITicketDetailRepository"/>.
/// Mirrors Java ticket_item JPA repository.
/// </summary>
public sealed class TicketDetailRepositoryImpl : ITicketDetailRepository
{
    private readonly FlashSaleDbContext _db;

    public TicketDetailRepositoryImpl(FlashSaleDbContext db) => _db = db;

    public Task<TicketDetail?> GetByIdAsync(long id, CancellationToken ct = default)
        => _db.TicketDetails.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<TicketDetail?> GetForUpdateAsync(long id, CancellationToken ct = default)
        => _db.TicketDetails.FromSqlRaw("SELECT * FROM ticket_item WHERE id = {0} FOR UPDATE", id)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<TicketDetail>> FindByActivityIdAsync(long activityId, CancellationToken ct = default)
        => await _db.TicketDetails.AsNoTracking()
            .Where(d => d.ActivityId == activityId)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

    public async Task<TicketDetail> AddAsync(TicketDetail detail, CancellationToken ct = default)
    {
        _db.TicketDetails.Add(detail);
        await _db.SaveChangesAsync(ct);
        return detail;
    }

    public async Task UpdateAsync(TicketDetail detail, CancellationToken ct = default)
    {
        _db.TicketDetails.Update(detail);
        await _db.SaveChangesAsync(ct);
    }

    public async Task IncreaseStockAsync(long id, int quantity, CancellationToken ct = default)
    {
        var d = await _db.TicketDetails.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return;
        d.StockAvailable += quantity;
        d.StockInitial = Math.Max(d.StockInitial, d.StockAvailable);
        d.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Atomic CAS-style decrement — wraps in transaction with row-level lock so concurrent
    /// callers can't oversell stock. Mirrors Java `decreaseStock` optimistic decrement.
    /// </summary>
    public async Task<bool> TryDecreaseStockAsync(long id, int quantity, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var d = await _db.TicketDetails
            .FromSqlRaw("SELECT * FROM ticket_item WHERE id = {0} FOR UPDATE", id)
            .FirstOrDefaultAsync(ct);
        if (d is null || d.StockAvailable < quantity) return false;
        d.StockAvailable -= quantity;
        d.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<int> GetStockAvailableAsync(long id, CancellationToken ct = default)
    {
        var d = await _db.TicketDetails.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return d?.StockAvailable ?? 0;
    }
}
