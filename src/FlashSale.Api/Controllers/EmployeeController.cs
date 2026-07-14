using System.Globalization;
using FlashSale.Application.Services;
using FlashSale.Application.Services.Implementations;
using FlashSale.Contracts.Dto;
using Microsoft.AspNetCore.Mvc;

namespace FlashSale.Api.Controllers;

/// <summary>
/// Employee timesheet controller — TASK-019 (port of Java EmployeeController).
///
/// Mirrors Java's exact routes under <c>/api/sign-in/{userId}</c>:
/// <list type="bullet">
///   <item>POST <c>/api/sign-in/{userId}</c>                       — sign-in now (UTC).</item>
///   <item>POST <c>/api/sign-in/{userId}/any-date</c>               — explicit date <c>?date=YYYY-MM-DD</c>.</item>
///   <item>GET  <c>/api/sign-in/{userId}/check</c>                  — has signed in on date.</item>
///   <item>GET  <c>/api/sign-in/{userId}/monthly-count</c>          — <c>?month=YYYY-MM</c>.</item>
///   <item>GET  <c>/api/sign-in/{userId}/monthly-sign-details</c>   — typed <c>MonthlySignDetailsDto</c>.</item>
///   <item>GET  <c>/api/sign-in/{userId}/first-day</c>              — -1 when no sign-in yet.</item>
///   <item>GET  <c>/api/sign-in/{userId}/consecutive-days</c>       — backward count.</item>
///   <item>GET  <c>/api/sign-in/{userId}/summary</c>                — composite <c>EmployeeSummaryDto</c>.</item>
/// </list>
///
/// Java returns raw types / <c>Map&lt;String,Object&gt;</c>. .NET wraps every response in
/// <see cref="ResultMessage{T}"/> + HTTP 200 — this differs from Java wire shape (see
/// KNOWN_DIFFERENCES.md §25), but is the convention everywhere else in the codebase.
/// </summary>
[ApiController]
[Route("api/sign-in/{userId}")]
public sealed class EmployeeController : ControllerBase
{
    private readonly IEmployeeCacheService _cache;
    private readonly ILogger<EmployeeController> _log;

    public EmployeeController(IEmployeeCacheService cache, ILogger<EmployeeController> log)
    {
        _cache = cache;
        _log = log;
    }

    // ---- helpers ----

    private static DateTime ParseDateOr(string? raw, DateTime fallback)
        => DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                  DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                  out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : fallback;

    private static DateTime ParseMonthOr(string? raw, DateTime fallback)
        => DateTime.TryParseExact($"{raw}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                  DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                  out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : fallback;

    private async Task<ResultMessage<T>> WrapAsync<T>(Func<Task<T>> op, string callSite)
    {
        try
        {
            var v = await op();
            return ResultMessage<T>.Data(v);
        }
        catch (ArgumentException ex)
        {
            _log.LogWarning(ex, "[Employee] invalid input at {Site}", callSite);
            return ResultMessage<T>.Error(400, ex.Message);
        }
        catch (EmployeeCacheException ex)
        {
            _log.LogError(ex, "[Employee] Redis failure at {Site}", callSite);
            return ResultMessage<T>.Error(503, "redis_unavailable");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[Employee] unhandled at {Site}", callSite);
            return ResultMessage<T>.Error(500, "internal");
        }
    }

    // ---- routes ----

    /// <summary>POST /api/sign-in/{userId} — sign-in for today (UTC).</summary>
    [HttpPost]
    public Task<ResultMessage<string>> SignInAsync(string userId, CancellationToken ct)
        => WrapAsync(async () =>
        {
            await _cache.SignInAsync(userId, ct);
            var now = DateTime.UtcNow;
            return $"Sign-in successful for {userId} at {now:yyyy-MM-dd}";
        }, "sign-in");

    /// <summary>POST /api/sign-in/{userId}/any-date?date=YYYY-MM-DD — sign-in for an arbitrary UTC date.</summary>
    [HttpPost("any-date")]
    public Task<ResultMessage<string>> SignInOnDateAsync(string userId, [FromQuery] string? date, CancellationToken ct)
        => WrapAsync(async () =>
        {
            var parsed = ParseDateOr(date, DateTime.UtcNow.Date);
            await _cache.SignInOnDateAsync(userId, parsed, ct);
            return $"Sign-in successful for user {userId} on {parsed:yyyy-MM-dd}";
        }, "sign-in/any-date");

    /// <summary>GET /api/sign-in/{userId}/check?date=YYYY-MM-DD — has the user signed in on this date?</summary>
    [HttpGet("check")]
    public Task<ResultMessage<bool>> HasSignedInAsync(string userId, [FromQuery] string? date, CancellationToken ct)
        => WrapAsync(() => _cache.HasSignedInAsync(userId, ParseDateOr(date, DateTime.UtcNow.Date), ct),
                     "check");

    /// <summary>GET /api/sign-in/{userId}/monthly-count?month=YYYY-MM — BITCOUNT.</summary>
    [HttpGet("monthly-count")]
    public Task<ResultMessage<long>> MonthlyCountAsync(string userId, [FromQuery] string? month, CancellationToken ct)
        => WrapAsync(() => _cache.GetMonthlyCountAsync(userId, ParseMonthOr(month, new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)), ct),
                     "monthly-count");

    /// <summary>GET /api/sign-in/{userId}/monthly-sign-details?month=YYYY-MM.</summary>
    [HttpGet("monthly-sign-details")]
    public Task<ResultMessage<MonthlySignDetailsDto>> MonthlySignDetailsAsync(string userId, [FromQuery] string? month, CancellationToken ct)
        => WrapAsync(() => _cache.GetMonthlySignDetailsAsync(userId, ParseMonthOr(month, new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)), ct),
                     "monthly-sign-details");

    /// <summary>GET /api/sign-in/{userId}/first-day?month=YYYY-MM — -1 when no sign-in yet.</summary>
    [HttpGet("first-day")]
    public Task<ResultMessage<int>> FirstSignDayAsync(string userId, [FromQuery] string? month, CancellationToken ct)
        => WrapAsync(() => _cache.GetFirstSignDayAsync(userId, ParseMonthOr(month, new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)), ct),
                     "first-day");

    /// <summary>GET /api/sign-in/{userId}/consecutive-days?date=YYYY-MM-DD.</summary>
    [HttpGet("consecutive-days")]
    public Task<ResultMessage<int>> ConsecutiveDaysAsync(string userId, [FromQuery] string? date, CancellationToken ct)
        => WrapAsync(() => _cache.GetConsecutiveDaysAsync(userId, ParseDateOr(date, DateTime.UtcNow.Date), ct),
                     "consecutive-days");

    /// <summary>GET /api/sign-in/{userId}/summary?date=YYYY-MM-DD — composite read.</summary>
    [HttpGet("summary")]
    public Task<ResultMessage<EmployeeSummaryDto>> SummaryAsync(string userId, [FromQuery] string? date, CancellationToken ct)
        => WrapAsync(() => _cache.GetSummaryAsync(userId, ParseDateOr(date, DateTime.UtcNow.Date), ct),
                     "summary");
}
