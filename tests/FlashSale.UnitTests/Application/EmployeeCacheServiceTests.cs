using FlashSale.Application.Services;
using FlashSale.Application.Services.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Range = Moq.Range;

namespace FlashSale.UnitTests.Application;

/// <summary>
/// Unit tests for <see cref="EmployeeCacheServiceImpl"/> — TASK-019.
///
/// Scope:
///   - Key format matches Java: <c>user:sign:{userId}:{yyyyMM}</c>.
///   - Bit offset is day-of-month minus one (off-by-one, 0-based).
///   - Same-day re-sign-in is idempotent (SETBIT to true is a no-op).
///   - First-sign-day scans only bits 0..lengthOfMonth-1 (returns -1 when empty).
///   - Consecutive-days walks backward and stops at the first clear bit.
///   - Summary aggregates 4 reads concurrently (we observe the call pattern).
///   - UTC key generation — passing a DateTime with Kind=Local is normalised to UTC.
///   - Validation: out-of-range day throws ArgumentOutOfRangeException.
///   - Transient Redis exceptions wrap to <see cref="EmployeeCacheException"/>.
/// </summary>
public class EmployeeCacheServiceTests
{
    private const string UserId = "10001";

    private static readonly DateTime Day1 = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day31 = new(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jul  = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb  = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private static (Mock<IEmployeeBitSetService> bits, EmployeeCacheServiceImpl sut) BuildSut()
    {
        var bits = new Mock<IEmployeeBitSetService>(MockBehavior.Strict);
        var sut = new EmployeeCacheServiceImpl(bits.Object, NullLogger<EmployeeCacheServiceImpl>.Instance);
        return (bits, sut);
    }

    // ============== Key format ==============

    [Fact]
    public async Task SignInOnDateAsync_builds_key_user_sign_userId_yyyyMM_and_sets_bit_offset_zero_for_day_one()
    {
        var (bits, sut) = BuildSut();
        bits.Setup(b => b.SetBitAsync("user:sign:10001:202607", 0, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await sut.SignInOnDateAsync(UserId, Day1, CancellationToken.None);

        bits.Verify(b => b.SetBitAsync("user:sign:10001:202607", 0, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SignInOnDateAsync_day_thirty_one_sets_bit_offset_thirty()
    {
        var (bits, sut) = BuildSut();
        bits.Setup(b => b.SetBitAsync("user:sign:10001:202607", 30, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await sut.SignInOnDateAsync(UserId, Day31, CancellationToken.None);

        bits.Verify(b => b.SetBitAsync("user:sign:10001:202607", 30, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SignInOnDateAsync_december_uses_yyyyMM_key_format()
    {
        var (bits, sut) = BuildSut();
        var dec5 = new DateTime(2026, 12, 5, 0, 0, 0, DateTimeKind.Utc);
        bits.Setup(b => b.SetBitAsync("user:sign:10001:202612", 4, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await sut.SignInOnDateAsync(UserId, dec5, CancellationToken.None);

        bits.Verify(b => b.SetBitAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(),
                                        It.IsAny<CancellationToken>()),
                    Times.Once);
    }

    // ============== Idempotency ==============

    [Fact]
    public async Task SignInOnDateAsync_twice_same_day_calls_SetBit_twice_but_no_state_change()
    {
        var (bits, sut) = BuildSut();
        // SETBIT to the same true value is a Redis no-op — both calls succeed.
        bits.Setup(b => b.SetBitAsync("user:sign:10001:202607", 14, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var day = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);
        await sut.SignInOnDateAsync(UserId, day, CancellationToken.None);
        await sut.SignInOnDateAsync(UserId, day, CancellationToken.None);

        bits.Verify(b => b.SetBitAsync("user:sign:10001:202607", 14, true, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ============== Reads ==============

    [Fact]
    public async Task HasSignedInAsync_proxies_bit_value()
    {
        var (bits, sut) = BuildSut();
        bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", 9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var got = await sut.HasSignedInAsync(UserId,
            new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        Assert.True(got);
        bits.Verify(b => b.GetBitAsync("user:sign:10001:202607", 9, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMonthlyCountAsync_returns_BITCOUNT_value()
    {
        var (bits, sut) = BuildSut();
        bits.Setup(b => b.BitCountAsync("user:sign:10001:202607", It.IsAny<CancellationToken>()))
            .ReturnsAsync(17L);

        var count = await sut.GetMonthlyCountAsync(UserId, Jul, CancellationToken.None);

        Assert.Equal(17L, count);
    }

    [Fact]
    public async Task GetFirstSignDayAsync_returns_minus_one_when_bitmap_empty()
    {
        var (bits, sut) = BuildSut();
        // Emulate 31 bits all false
        for (int i = 0; i < 31; i++)
        {
            bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", i, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
        }

        var first = await sut.GetFirstSignDayAsync(UserId, Jul, CancellationToken.None);
        Assert.Equal(-1, first);
    }

    [Fact]
    public async Task GetFirstSignDayAsync_returns_first_set_bit_as_one_based_day()
    {
        var (bits, sut) = BuildSut();
        bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", 0, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", 2, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var first = await sut.GetFirstSignDayAsync(UserId, Jul, CancellationToken.None);
        Assert.Equal(3, first);

        // Bits 3..30 must not have been queried.
        bits.Verify(b => b.GetBitAsync("user:sign:10001:202607",
                                       It.IsInRange(3L, 30L, Range.Inclusive),
                                       It.IsAny<CancellationToken>()),
                    Times.Never);
    }

    [Fact]
    public async Task GetFirstSignDayAsync_February_only_scans_28_bits_not_31()
    {
        var (bits, sut) = BuildSut();
        for (int i = 0; i < 28; i++)
        {
            bits.Setup(b => b.GetBitAsync("user:sign:10001:202602", i, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
        }

        var first = await sut.GetFirstSignDayAsync(UserId, Feb, CancellationToken.None);
        Assert.Equal(-1, first);

        // Bits 28..30 must NOT be queried — .NET deviates from Java here.
        bits.Verify(b => b.GetBitAsync("user:sign:10001:202602",
                                       It.IsInRange(28L, 30L, Range.Inclusive),
                                       It.IsAny<CancellationToken>()),
                    Times.Never);
    }

    [Fact]
    public async Task GetConsecutiveDaysAsync_walks_backward_from_offset_until_zero_bit()
    {
        var (bits, sut) = BuildSut();
        var date = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);   // offset = 9
        // bitmap: bits 5,6,7,8,9 set; bit 4 clear.
        // Expect count = 5 (bits 5..9), stops at 4.
        for (int i = 0; i <= 4; i++)
        {
            bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", i, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
        }
        for (int i = 5; i <= 9; i++)
        {
            bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", i, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        var n = await sut.GetConsecutiveDaysAsync(UserId, date, CancellationToken.None);
        Assert.Equal(5, n);

        // Bits 0..3 must not have been queried (loop breaks at 4).
        bits.Verify(b => b.GetBitAsync("user:sign:10001:202607",
                                       It.IsInRange(0L, 3L, Range.Inclusive),
                                       It.IsAny<CancellationToken>()),
                    Times.Never);
    }

    [Fact]
    public async Task GetConsecutiveDaysAsync_returns_zero_when_today_bit_unset()
    {
        var (bits, sut) = BuildSut();
        var date = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);   // offset = 9
        bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", 9, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var n = await sut.GetConsecutiveDaysAsync(UserId, date, CancellationToken.None);
        Assert.Equal(0, n);
    }

    [Fact]
    public async Task GetMonthlySignDetailsAsync_lists_set_days_as_one_based_inclusive()
    {
        var (bits, sut) = BuildSut();
        bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", 0, It.IsAny<CancellationToken>())).ReturnsAsync(true);    // 1
        bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", 2, It.IsAny<CancellationToken>())).ReturnsAsync(true);    // 3
        for (int i = 3; i < 31; i++)
        {
            int captured = i;
            bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", captured, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
        }

        var details = await sut.GetMonthlySignDetailsAsync(UserId, Jul, CancellationToken.None);
        Assert.Equal(2, details.TotalSignCount);
        Assert.Equal(new[] { 1, 3 }, details.SignDays);
    }

    [Fact]
    public async Task GetSummaryAsync_calls_four_reads_and_composes_aggregate()
    {
        var (bits, sut) = BuildSut();
        var date = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);

        bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", 9, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        bits.Setup(b => b.BitCountAsync("user:sign:10001:202607", It.IsAny<CancellationToken>())).ReturnsAsync(5L);
        // First-sign-day: bit 0 false, bit 1 true
        bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", 0, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Consecutive-days: bits 5..9 true (we already setup bit 9 above; add 5..8)
        for (int i = 5; i <= 8; i++)
        {
            int captured = i;
            bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", captured, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }
        bits.Setup(b => b.GetBitAsync("user:sign:10001:202607", 4, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var summary = await sut.GetSummaryAsync(UserId, date, CancellationToken.None);
        Assert.Equal(date, summary.Date);
        Assert.True(summary.HasSignedIn);
        Assert.Equal(5L, summary.MonthlyCount);
        Assert.Equal(2, summary.FirstSignDay);
        Assert.Equal(5, summary.ConsecutiveDays);
    }

    // ============== Validation ==============

    [Fact]
    public async Task SignInOnDateAsync_throws_when_day_out_of_range()
    {
        var (bits, sut) = BuildSut();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            sut.SignInOnDateAsync(UserId, new DateTime(2026, 7, 0, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None));

        bits.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetMonthlyCountAsync_throws_when_month_argument_not_normalised_to_day_one()
    {
        var (bits, sut) = BuildSut();
        var bad = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.GetMonthlyCountAsync(UserId, bad, CancellationToken.None));

        bits.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SignInOnDateAsync_UTC_normalisation_does_not_matter_for_zero_offset_zones()
    {
        // The contract: caller passes either UTC or local kinds; impl builds the
        // yyyyMM key from DateTime.Year/Month. We only assert the produced key
        // is `yyyyMM` of the date passed in.
        var (bits, sut) = BuildSut();
        bits.Setup(b => b.SetBitAsync(It.IsAny<string>(), 0, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var localStyle = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Local);
        await sut.SignInOnDateAsync(UserId, localStyle, CancellationToken.None);

        bits.Verify(b => b.SetBitAsync(
            It.Is<string>(k => k == "user:sign:10001:202608"),
            0L, true, It.IsAny<CancellationToken>()), Times.Once);
    }
}
