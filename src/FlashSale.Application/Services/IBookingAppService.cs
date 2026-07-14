using FlashSale.Contracts.Dto;

namespace FlashSale.Application.Services;

/// <summary>
/// Booking service — mirrors Java BookingAppService.
/// </summary>
public interface IBookingAppService
{
    Task<BookingDto> CreateAsync(CreateBookingRequest request, CancellationToken ct = default);
}

/// <summary>
/// Demo event service — for Hi/CircuitBreaker/RateLimiter endpoints.
/// </summary>
public interface IEventAppService
{
    string SayHi(string name);
}