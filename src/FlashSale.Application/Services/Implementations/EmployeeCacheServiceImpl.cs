using System.Globalization;
using FlashSale.Application.Services;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Services.Implementations;

/// <summary>
/// Employee timesheet cache — TASK-019 (port of Java EmployeeCacheService).
///
/// Backed by Redis BitSet via the abstraction <see cref="IEmployeeBitSetService"/>:
///   - SETBIT → <c>SetBitAsync(key, offset, true)</c>
///   - GETBIT → <c>GetBitAsync(key, offset)</c>
///   - BITCOUNT → <c>BitCountAsync(key)</c>
///
/// Key format: <c>user:sign:{userId}:{yyyyMM}</c> — matches Java exactly. Keys
/// are namespaced under <c>user:sign:</c> so a future cross-month Redis SCAN
/// (e.g. for batch reporting) can find them without colliding with
/// <c>ticket:</c>/<c>order:</c>/<c>stock:</c> namespaces used elsewhere.
///
/// All callers go through <see cref="ExecuteAsync{T}"/> so a Redis outage surfaces
/// as <see cref="EmployeeCacheException"/> with a stable shape that the controller
/// maps to a 5xx <c>ResultMessage&lt;object&gt;</c>.
/// </summary>
public sealed class EmployeeCacheServiceImpl : IEmployeeCacheService
{
    private readonly IEmployeeBitSetService _bits;
    private readonly ILogger<EmployeeCacheServiceImpl> _log;

    public EmployeeCacheServiceImpl(IEmployeeBitSetService bits, ILogger<EmployeeCacheServiceImpl> log)
    {
        _bits = bits;
        _log = log;
    }

    // ============== Writes ==============

    public Task SignInAsync(string userId, CancellationToken ct = default)
    {
        var utcNow = DateTime.UtcNow;
        return SignInOnDateAsync(userId, utcNow, ct);
    }

    public async Task SignInOnDateAsync(string userId, DateTime date, CancellationToken ct = default)
    {
        ValidateDay(date);
        var (key, offset) = ResolveKey(userId, date);
        await ExecuteAsync<bool>(async () =>
        {
            await _bits.SetBitAsync(key, offset, true);
            return true;
        }, $"SignIn({userId},{date:yyyy-MM-dd})");
    }

    // ============== Reads ==============

    public async Task<bool> HasSignedInAsync(string userId, DateTime date, CancellationToken ct = default)
    {
        ValidateDay(date);
        var (key, offset) = ResolveKey(userId, date);
        return await ExecuteAsync(() => _bits.GetBitAsync(key, offset),
                                  $"HasSignedIn({userId},{date:yyyy-MM-dd})");
    }

    public async Task<long> GetMonthlyCountAsync(string userId, DateTime month, CancellationToken ct = default)
    {
        ValidateMonth(month);
        var key = BuildMonthKey(userId, month);
        return await ExecuteAsync(() => _bits.BitCountAsync(key),
                                  $"GetMonthlyCount({userId},{month:yyyy-MM})");
    }

    public async Task<int> GetFirstSignDayAsync(string userId, DateTime month, CancellationToken ct = default)
    {
        ValidateMonth(month);
        var (key, _) = ResolveKey(userId, month);
        var lastDay = LastDayOfMonth(month);
        return await ExecuteAsync(async () =>
        {
            // .NET deviates from Java: scan only 0..lastDay-1, not 0..30.
            // See KNOWN_DIFFERENCES.md §22.
            for (int i = 0; i <= lastDay - 1; i++)
            {
                if (await _bits.GetBitAsync(key, i))
                    return i + 1;             // day-of-month (1-based)
            }
            return -1;
        }, $"GetFirstSignDay({userId},{month:yyyy-MM})");
    }

    public async Task<int> GetConsecutiveDaysAsync(string userId, DateTime date, CancellationToken ct = default)
    {
        ValidateDay(date);
        var (key, endOffset) = ResolveKey(userId, date);
        return await ExecuteAsync(async () =>
        {
            var count = 0;
            // Walk backward from `endOffset` (dayOfMonth-1) to bit 0.
            // Stop at the first clear bit. Does NOT cross the month boundary
            // (matches Java, see KNOWN_DIFFERENCES.md §23).
            for (int i = (int)endOffset; i >= 0; i--)
            {
                if (await _bits.GetBitAsync(key, i))
                    count++;
                else
                    break;
            }
            return count;
        }, $"GetConsecutiveDays({userId},{date:yyyy-MM-dd})");
    }

    public async Task<MonthlySignDetailsDto> GetMonthlySignDetailsAsync(string userId, DateTime month, CancellationToken ct = default)
    {
        ValidateMonth(month);
        var (key, _) = ResolveKey(userId, month);
        var lastDay = LastDayOfMonth(month);
        return await ExecuteAsync(async () =>
        {
            var days = new List<int>(lastDay);
            for (int i = 0; i <= lastDay - 1; i++)
            {
                if (await _bits.GetBitAsync(key, i))
                    days.Add(i + 1);          // 1-based day-of-month
            }
            return new MonthlySignDetailsDto
            {
                TotalSignCount = days.Count,
                SignDays = days,
            };
        }, $"GetMonthlySignDetails({userId},{month:yyyy-MM})");
    }

    public async Task<EmployeeSummaryDto> GetSummaryAsync(string userId, DateTime date, CancellationToken ct = default)
    {
        ValidateDay(date);
        var monthStart = new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Parallelise the 4 reads — they're on different keys (date vs month)
        // and only `HasSignedIn` shares the month key with `MonthlyCount`,
        // but Redis multiplexes that.
        var hasSignedInTask    = HasSignedInAsync(userId, date, ct);
        var monthlyCountTask   = GetMonthlyCountAsync(userId, monthStart, ct);
        var firstSignDayTask   = GetFirstSignDayAsync(userId, monthStart, ct);
        var consecutiveTask    = GetConsecutiveDaysAsync(userId, date, ct);

        await Task.WhenAll(hasSignedInTask, monthlyCountTask, firstSignDayTask, consecutiveTask);

        return new EmployeeSummaryDto
        {
            Date             = date,
            HasSignedIn      = hasSignedInTask.Result,
            MonthlyCount     = monthlyCountTask.Result,
            FirstSignDay     = firstSignDayTask.Result,
            ConsecutiveDays  = consecutiveTask.Result,
        };
    }

    // ============== Helpers ==============

    /// <summary>
    /// Compose the Redis key + the bit offset for a (userId, calendar date).
    /// </summary>
    private static (string key, long offset) ResolveKey(string userId, DateTime date)
        => (BuildMonthKey(userId, date), date.Day - 1);

    private static string BuildMonthKey(string userId, DateTime monthUtc)
        => $"user:sign:{userId}:{monthUtc.ToString("yyyyMM", CultureInfo.InvariantCulture)}";

    private static int LastDayOfMonth(DateTime monthUtc)
        => DateTime.DaysInMonth(monthUtc.Year, monthUtc.Month);

    private static void ValidateDay(DateTime date)
    {
        if (date.Day < 1 || date.Day > 31)
            throw new ArgumentOutOfRangeException(nameof(date), $"Day-of-month must be 1..31 (got {date.Day}).");
    }

    private static void ValidateMonth(DateTime monthUtc)
    {
        if (monthUtc.Day != 1)
            throw new ArgumentException("Month argument must be normalised to day=1 (call .AddDays(1 - day) yourself).", nameof(monthUtc));
        if (monthUtc.Month < 1 || monthUtc.Month > 12)
            throw new ArgumentOutOfRangeException(nameof(monthUtc), $"Month must be 1..12 (got {monthUtc.Month}).");
    }

    /// <summary>
    /// Run a bit operation against the underlying Redis wrapper with a uniform error envelope.
    /// Connection failures / timeouts surface as <see cref="EmployeeCacheException"/>;
    /// server-level errors propagate so they show up in tests.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<Task<T>> op, string callSite)
    {
        try
        {
            return await op();
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            _log.LogError(ex, "[EmployeeCache] Redis transient failure at {Site}", callSite);
            throw new EmployeeCacheException($"Redis unavailable: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Tests transient exception classes by namespace + name to avoid referencing
    /// StackExchange.Redis types directly from the Application assembly (which
    /// intentionally has no Infrastructure dependency).
    /// </summary>
    private static bool IsTransient(Exception ex)
    {
        var n = ex.GetType().FullName ?? string.Empty;
        return n == "StackExchange.Redis.RedisConnectionException"
            || n == "StackExchange.Redis.RedisTimeoutException";
    }
}

/// <summary>Thin marker exception so the controller can produce a stable error code.</summary>
public sealed class EmployeeCacheException : Exception
{
    public EmployeeCacheException(string message, Exception inner) : base(message, inner) { }
}
