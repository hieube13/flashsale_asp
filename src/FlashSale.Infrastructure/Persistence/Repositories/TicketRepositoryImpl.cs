using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using FlashSale.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ITicketRepository"/>.
/// Mirrors Java ticket JPA repository.
/// </summary>
public sealed class TicketRepositoryImpl : ITicketRepository
{
    private readonly FlashSaleDbContext _db;

    public TicketRepositoryImpl(FlashSaleDbContext db) => _db = db;

    public Task<Ticket?> GetByIdAsync(long id, CancellationToken ct = default)
        => _db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<Ticket>> GetActiveAsync(CancellationToken ct = default)
        => await _db.Tickets.AsNoTracking()
            .Where(t => t.Status == 1)
            .OrderBy(t => t.StartTime)
            .ToListAsync(ct);

    public async Task<Ticket> AddAsync(Ticket ticket, CancellationToken ct = default)
    {
        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(ct);
        return ticket;
    }

    public async Task UpdateAsync(Ticket ticket, CancellationToken ct = default)
    {
        _db.Tickets.Update(ticket);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SoftDeleteAsync(long id, CancellationToken ct = default)
    {
        var t = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return;
        t.Status = 2; // DELETED
        t.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
