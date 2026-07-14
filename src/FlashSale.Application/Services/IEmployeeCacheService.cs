namespace FlashSale.Application.Services;

/// <summary>
/// Abstraction over Redis BitSet primitives — used by <see cref="IEmployeeCacheService"/>.
/// Lives in Application (not Infrastructure) so the cache service itself can stay
/// out of Infrastructure's assembly, matching the architecture graph for every
/// other app-layer service. The concrete implementation lives in
/// <c>FlashSale.Infrastructure.Cache.EmployeeBitSetService</c>.
/// </summary>
public interface IEmployeeBitSetService
{
    /// <summary>SETBIT key offset true.</summary>
    Task SetBitAsync(string key, long offset, bool value, CancellationToken ct = default);

    /// <summary>GETBIT key offset.</summary>
    Task<bool> GetBitAsync(string key, long offset, CancellationToken ct = default);

    /// <summary>BITCOUNT key.</summary>
    Task<long> BitCountAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// Employee timesheet service — uses Redis BitSet for monthly attendance bitmap.
/// Mirrors Java EmployeeCacheService (`xxxx-application/.../service/employee/cache/EmployeeCacheService.java`).
///
/// All keys use UTC and the pattern <c>user:sign:{userId}:{yyyyMM}</c>; bit-index
/// for a given day = <c>dayOfMonth - 1</c> (off-by-one, 0-based). Java used
/// <c>LocalDate</c> (server-local zone) — .NET pins UTC to avoid timezone drift
/// across containers (see KNOWN_DIFFERENCES.md §21).
/// </summary>
public interface IEmployeeCacheService
{
    /// <summary>Set today's bit (UTC).</summary>
    Task SignInAsync(string userId, CancellationToken ct = default);

    /// <summary>Set the bit for an arbitrary UTC date.</summary>
    Task SignInOnDateAsync(string userId, DateTime date, CancellationToken ct = default);

    /// <summary>Read the bit for a given UTC date.</summary>
    Task<bool> HasSignedInAsync(string userId, DateTime date, CancellationToken ct = default);

    /// <summary>BITCOUNT over <c>user:sign:{userId}:{month:yyyyMM}</c>.</summary>
    Task<long> GetMonthlyCountAsync(string userId, DateTime month, CancellationToken ct = default);

    /// <summary>
    /// First bit set in <c>user:sign:{userId}:{month:yyyyMM}</c>. Returns day-of-month,
    /// or <c>-1</c> when the bitmap is empty.
    /// .NET deviates from Java: scans only <c>0..lengthOfMonth-1</c> (Java scanned 0..30
    /// unconditionally). See KNOWN_DIFFERENCES.md §22.
    /// </summary>
    Task<int> GetFirstSignDayAsync(string userId, DateTime month, CancellationToken ct = default);

    /// <summary>
    /// Walk backward from the bit for <paramref name="date"/>'s day, counting consecutive
    /// set bits. Stops at the first clear bit. Does NOT cross the month boundary —
    /// matches Java (see KNOWN_DIFFERENCES.md §23).
    /// </summary>
    Task<int> GetConsecutiveDaysAsync(string userId, DateTime date, CancellationToken ct = default);

    /// <summary>
    /// Returns the list of days (1-based) the user has signed in during the given month.
    /// Mirrors Java <c>getMonthlySignDetails()</c> but as a typed DTO rather than a
    /// <c>Map&lt;String,Object&gt;</c> (see KNOWN_DIFFERENCES.md §24).
    /// </summary>
    Task<MonthlySignDetailsDto> GetMonthlySignDetailsAsync(string userId, DateTime month, CancellationToken ct = default);

    /// <summary>
    /// Aggregate convenience: <c>{date, hasSignedIn, monthlyCount, firstSignDay, consecutiveDays}</c>
    /// for the given date. Mirrors Java <c>summary()</c> — composes 4 reads.
    /// </summary>
    Task<EmployeeSummaryDto> GetSummaryAsync(string userId, DateTime date, CancellationToken ct = default);
}

/// <summary>Result of <see cref="IEmployeeCacheService.GetMonthlySignDetailsAsync"/>.</summary>
public sealed class MonthlySignDetailsDto
{
    public int TotalSignCount { get; set; }
    public IReadOnlyList<int> SignDays { get; set; } = Array.Empty<int>();
}

/// <summary>Result of <see cref="IEmployeeCacheService.GetSummaryAsync"/>.</summary>
public sealed class EmployeeSummaryDto
{
    public DateTime Date { get; set; }
    public bool HasSignedIn { get; set; }
    public long MonthlyCount { get; set; }
    public int FirstSignDay { get; set; }
    public int ConsecutiveDays { get; set; }
}
