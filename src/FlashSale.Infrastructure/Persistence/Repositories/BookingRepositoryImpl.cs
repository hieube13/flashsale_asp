using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using FlashSale.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IBookingRepository"/> — TASK-020.
///
/// Mirrors Java <c>BookingRepositoryImpl</c> (<c>xxxx-infrastructure/.../BookingRepositoryImpl.java</c>),
/// which delegates to the underlying Spring Data JPA mapper. Java exposes <c>findByBookingCode</c>
/// in the interface but no consumer uses it — .NET matches by NOT carrying that method forward
/// (parity, see KNOWN_DIFFERENCES.md §26).
/// </summary>
public sealed class BookingRepositoryImpl : IBookingRepository
{
    private readonly FlashSaleDbContext _db;

    public BookingRepositoryImpl(FlashSaleDbContext db) => _db = db;

    public async Task<Booking> AddAsync(Booking booking, CancellationToken ct = default)
    {
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync(ct);
        return booking;
    }

    public async Task<Booking?> GetByIdAsync(long id, CancellationToken ct = default)
        => await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
}
