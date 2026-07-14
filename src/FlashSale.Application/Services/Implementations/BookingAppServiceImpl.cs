using FlashSale.Contracts.Dto;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Services.Implementations;

/// <summary>
/// Booking app service — TASK-020 (port of Java <c>BookingAppServiceImpl</c>).
///
/// Pipeline (mirrors Java <c>BookingAppServiceImpl.createBooking</c> lines 19-31):
///   1. Validate <paramref name="request"/> (ticketId ≥ 1, quantity 1..10).
///   2. Build the <see cref="Booking"/> entity.
///   3. Persist via <see cref="IBookingRepository.AddAsync"/>.
///   4. Return <see cref="BookingDto"/> WITHOUT <c>CreatedAt</c> (parity with Java DTO).
///
/// <para>
/// Booking code format matches Java: <c>"BK" + currentTimeMillis() + 4-char uppercase hex</c>
/// (e.g. <c>BK1718000000000ABCD</c>). No idempotency / Redis cache / Kafka publish here.
/// </para>
/// </summary>
public sealed class BookingAppServiceImpl : IBookingAppService
{
    private const int MaxQuantity = 10;

    private readonly IBookingRepository _bookings;
    private readonly ILogger<BookingAppServiceImpl> _log;

    public BookingAppServiceImpl(IBookingRepository bookings, ILogger<BookingAppServiceImpl> log)
    {
        _bookings = bookings;
        _log = log;
    }

    public async Task<BookingDto> CreateAsync(CreateBookingRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.TicketId <= 0)
            throw new ArgumentException("ticketId must be > 0", nameof(request));
        if (request.Quantity < 1 || request.Quantity > MaxQuantity)
            throw new ArgumentException($"quantity must be 1..{MaxQuantity} (got {request.Quantity})", nameof(request));

        var booking = new Booking
        {
            TicketId    = request.TicketId,
            Quantity    = request.Quantity,
            BookingCode = GenerateBookingCode(),
            Status      = 1,                 // CONFIRMED — matches Java BookingDomainServiceImpl line 36
            CreatedAt   = DateTime.UtcNow,
        };

        var saved = await _bookings.AddAsync(booking, ct);

        _log.LogInformation(
            "[Booking] Created id={Id} code={Code} ticketId={TicketId} quantity={Quantity}",
            saved.Id, saved.BookingCode, saved.TicketId, saved.Quantity);

        return new BookingDto(
            Id:          saved.Id,
            TicketId:    saved.TicketId,
            Quantity:    saved.Quantity,
            BookingCode: saved.BookingCode,
            Status:      saved.Status);
    }

    /// <summary>
    /// Mirrors Java <c>BookingDomainServiceImpl.generateBookingCode</c> line 44-47:
    /// <c>"BK" + currentTimeMillis() + 4-hex-char substring(UUID.randomUUID())</c>.
    /// </summary>
    internal static string GenerateBookingCode()
        => "BK" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                 + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpperInvariant();
}
