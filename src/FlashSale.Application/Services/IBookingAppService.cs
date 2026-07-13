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
/// Employee timesheet service — uses Redis BitSet for monthly attendance bitmap.
/// Mirrors Java EmployeeCacheService.
/// </summary>
public interface IEmployeeCacheService
{
    Task SignInAsync(string userId, CancellationToken ct = default);
    Task SignInOnDateAsync(string userId, DateTime date, CancellationToken ct = default);
    Task<bool> HasSignedInAsync(string userId, DateTime date, CancellationToken ct = default);
    Task<long> GetMonthlyCountAsync(string userId, DateTime month, CancellationToken ct = default);
    Task<int> GetFirstSignDayAsync(string userId, DateTime month, CancellationToken ct = default);
    Task<int> GetConsecutiveDaysAsync(string userId, DateTime date, CancellationToken ct = default);
}

/// <summary>
/// Demo event service — for Hi/CircuitBreaker/RateLimiter endpoints.
/// </summary>
public interface IEventAppService
{
    string SayHi(string name);
}