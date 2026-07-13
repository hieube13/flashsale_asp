using FlashSale.Application.Services;
using FlashSale.Application.Services.Implementations;
using FlashSale.Domain.Repositories;
using FlashSale.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlashSale.UnitTests.Application;

/// <summary>
/// Unit tests for <see cref="TicketOrderAppServiceImpl.CancelOrderAsync"/> —
/// TASK-014 (order cancel slice).
///
/// Mirrors the cancellation pipeline in Java TicketOrderAppServiceImpl
/// lines 439-511:
///   1. Acquire distributed lock <c>LOCK:CANCEL_ORDER:{orderNumber}</c>
///      (wait 1 s, expiry 5 s).
///   2. Resolve year-month shard from order_number.
///   3. Fetch order by order_number; ownership check.
///   4. Idempotent if already CANCELLED (status=2).
///   5. Update status to CANCELLED via Dapper.
///   6. Restore DB stock.
///   7. Best-effort Redis stock restore.
/// </summary>
public class TicketOrderCancelOrderAsyncTests
{
    private const long UserId = 7L;
    private const long OtherUserId = 99L;
    private const int CancelledStatus = 2;
    private const int SuccessStatus = 1;
    private const string OrderNumber = "OKX-SGN-7-42-1721035200000";
    private const string YearMonth = "202407";
    private const int TicketId = 11;
    private const int Quantity = 2;

    private static readonly DateTime OrderDate = new(2024, 7, 15, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Builds a row array matching the column ordering in the
    /// <c>ticket_order_{yyyyMM}</c> table (12 columns).
    /// </summary>
    private static object?[] SampleRow(int userId, int orderStatus)
        => new object?[]
        {
            1L,                     // 0  id
            (long)userId,           // 1  user_id
            (long)TicketId,         // 2  ticket_id
            Quantity,               // 3  quantity
            orderStatus,            // 4  order_status
            OrderNumber,            // 5  order_number
            20000L,                 // 6  total_amount
            "TERM-001",             // 7  terminal_id
            OrderDate,              // 8  order_date
            null,                   // 9  order_notes
            OrderDate,              // 10 updated_at
            OrderDate,              // 11 created_at
        };

    private sealed class LockHarness
    {
        public Mock<IDistributedLockProvider> Provider { get; }
        public Mock<IDistributedLock> Handle { get; }
        public bool Released { get; private set; }
        public LockHarness(bool acquired)
        {
            Provider = new Mock<IDistributedLockProvider>(MockBehavior.Strict);
            Handle = new Mock<IDistributedLock>(MockBehavior.Strict);
            Handle.Setup(h => h.TryAcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(acquired);
            Handle.Setup(h => h.ReleaseAsync(It.IsAny<CancellationToken>()))
                  .Callback(() => Released = true)
                  .Returns(Task.CompletedTask);
            Provider.Setup(p => p.GetLock(It.IsAny<string>())).Returns(Handle.Object);
        }
    }

    private static TicketOrderAppServiceImpl BuildSut(
        Mock<ITickerOrderRepository> orders,
        Mock<IOrderDeductionDomainService> domain,
        Mock<ITicketDetailRepository> details,
        Mock<IStockOrderCacheService> stockCache,
        LockHarness locks)
        => new(orders.Object, domain.Object, details.Object, stockCache.Object,
               locks.Provider.Object, NullLogger<TicketOrderAppServiceImpl>.Instance);

    // ============== Happy path ==============

    [Fact]
    public async Task CancelOrderAsync_happy_path_restores_stock_and_returns_true()
    {
        var orders = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        var details = new Mock<ITicketDetailRepository>(MockBehavior.Strict);
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var locks = new LockHarness(acquired: true);

        domain.Setup(d => d.ExtractYearMonth(OrderNumber)).Returns(YearMonth);
        orders.Setup(o => o.FindByOrderNumberAsync(YearMonth, OrderNumber, It.IsAny<CancellationToken>()))
              .ReturnsAsync(SampleRow((int)UserId, SuccessStatus));
        orders.Setup(o => o.UpdateStatusAsync(YearMonth, OrderNumber, CancelledStatus, It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        details.Setup(d => d.IncreaseStockAsync(TicketId, Quantity, It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        stockCache.Setup(s => s.IncreaseStockCacheAsync(TicketId, Quantity, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true);

        var sut = BuildSut(orders, domain, details, stockCache, locks);
        var result = await sut.CancelOrderAsync(UserId, OrderNumber);

        Assert.True(result);
        locks.Handle.Verify(h => h.TryAcquireAsync(
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(locks.Released, "lock handle must be released in finally");
        orders.Verify(o => o.UpdateStatusAsync(YearMonth, OrderNumber, CancelledStatus, It.IsAny<CancellationToken>()), Times.Once);
        details.Verify(d => d.IncreaseStockAsync(TicketId, Quantity, It.IsAny<CancellationToken>()), Times.Once);
        stockCache.Verify(s => s.IncreaseStockCacheAsync(TicketId, Quantity, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelOrderAsync_uses_lock_key_LOCK_CANCEL_ORDER_orderNumber()
    {
        var orders = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        var details = new Mock<ITicketDetailRepository>(MockBehavior.Strict);
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var locks = new LockHarness(acquired: true);

        domain.Setup(d => d.ExtractYearMonth(OrderNumber)).Returns(YearMonth);
        orders.Setup(o => o.FindByOrderNumberAsync(YearMonth, OrderNumber, It.IsAny<CancellationToken>()))
              .ReturnsAsync(SampleRow((int)UserId, SuccessStatus));
        orders.Setup(o => o.UpdateStatusAsync(YearMonth, OrderNumber, CancelledStatus, It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        details.Setup(d => d.IncreaseStockAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        stockCache.Setup(s => s.IncreaseStockCacheAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true);

        var sut = BuildSut(orders, domain, details, stockCache, locks);
        await sut.CancelOrderAsync(UserId, OrderNumber);

        locks.Provider.Verify(p => p.GetLock($"LOCK:CANCEL_ORDER:{OrderNumber}"), Times.Once);
    }

    // ============== Lock busy ==============

    [Fact]
    public async Task CancelOrderAsync_returns_false_when_lock_busy_and_does_not_call_db()
    {
        var orders = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        var details = new Mock<ITicketDetailRepository>(MockBehavior.Strict);
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var locks = new LockHarness(acquired: false);

        var sut = BuildSut(orders, domain, details, stockCache, locks);
        var result = await sut.CancelOrderAsync(UserId, OrderNumber);

        Assert.False(result);
        orders.Verify(o => o.FindByOrderNumberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        details.Verify(d => d.IncreaseStockAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        stockCache.Verify(s => s.IncreaseStockCacheAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        // Lock is acquired=false → release still attempted (cleanup is best-effort).
        Assert.True(locks.Released, "release must run even when acquisition fails");
    }

    // ============== Order not found ==============

    [Fact]
    public async Task CancelOrderAsync_returns_false_when_order_not_found()
    {
        var orders = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        var details = new Mock<ITicketDetailRepository>(MockBehavior.Strict);
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var locks = new LockHarness(acquired: true);

        domain.Setup(d => d.ExtractYearMonth(OrderNumber)).Returns(YearMonth);
        orders.Setup(o => o.FindByOrderNumberAsync(YearMonth, OrderNumber, It.IsAny<CancellationToken>()))
              .ReturnsAsync((object?[]?)null);

        var sut = BuildSut(orders, domain, details, stockCache, locks);
        var result = await sut.CancelOrderAsync(UserId, OrderNumber);

        Assert.False(result);
        orders.Verify(o => o.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        details.Verify(d => d.IncreaseStockAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ============== Ownership mismatch ==============

    [Fact]
    public async Task CancelOrderAsync_returns_false_when_userId_does_not_match()
    {
        var orders = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        var details = new Mock<ITicketDetailRepository>(MockBehavior.Strict);
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var locks = new LockHarness(acquired: true);

        domain.Setup(d => d.ExtractYearMonth(OrderNumber)).Returns(YearMonth);
        orders.Setup(o => o.FindByOrderNumberAsync(YearMonth, OrderNumber, It.IsAny<CancellationToken>()))
              .ReturnsAsync(SampleRow((int)OtherUserId, SuccessStatus));

        var sut = BuildSut(orders, domain, details, stockCache, locks);
        var result = await sut.CancelOrderAsync(UserId, OrderNumber);

        Assert.False(result);
        orders.Verify(o => o.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        details.Verify(d => d.IncreaseStockAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ============== Idempotent: already cancelled ==============

    [Fact]
    public async Task CancelOrderAsync_returns_true_immediately_when_already_cancelled_and_does_not_restore_stock()
    {
        var orders = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        var details = new Mock<ITicketDetailRepository>(MockBehavior.Strict);
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var locks = new LockHarness(acquired: true);

        domain.Setup(d => d.ExtractYearMonth(OrderNumber)).Returns(YearMonth);
        orders.Setup(o => o.FindByOrderNumberAsync(YearMonth, OrderNumber, It.IsAny<CancellationToken>()))
              .ReturnsAsync(SampleRow((int)UserId, CancelledStatus));

        var sut = BuildSut(orders, domain, details, stockCache, locks);
        var result = await sut.CancelOrderAsync(UserId, OrderNumber);

        Assert.True(result);
        // Critical: status must not be flipped again, stock must not be restored.
        orders.Verify(o => o.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        details.Verify(d => d.IncreaseStockAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        stockCache.Verify(s => s.IncreaseStockCacheAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ============== Status update failure ==============

    [Fact]
    public async Task CancelOrderAsync_returns_false_when_db_status_update_fails()
    {
        var orders = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        var details = new Mock<ITicketDetailRepository>(MockBehavior.Strict);
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var locks = new LockHarness(acquired: true);

        domain.Setup(d => d.ExtractYearMonth(OrderNumber)).Returns(YearMonth);
        orders.Setup(o => o.FindByOrderNumberAsync(YearMonth, OrderNumber, It.IsAny<CancellationToken>()))
              .ReturnsAsync(SampleRow((int)UserId, SuccessStatus));
        orders.Setup(o => o.UpdateStatusAsync(YearMonth, OrderNumber, CancelledStatus, It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        var sut = BuildSut(orders, domain, details, stockCache, locks);
        var result = await sut.CancelOrderAsync(UserId, OrderNumber);

        Assert.False(result);
        details.Verify(d => d.IncreaseStockAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        stockCache.Verify(s => s.IncreaseStockCacheAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ============== Redis restore failure ==============

    [Fact]
    public async Task CancelOrderAsync_returns_true_even_if_redis_restore_fails()
    {
        var orders = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        var details = new Mock<ITicketDetailRepository>(MockBehavior.Strict);
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var locks = new LockHarness(acquired: true);

        domain.Setup(d => d.ExtractYearMonth(OrderNumber)).Returns(YearMonth);
        orders.Setup(o => o.FindByOrderNumberAsync(YearMonth, OrderNumber, It.IsAny<CancellationToken>()))
              .ReturnsAsync(SampleRow((int)UserId, SuccessStatus));
        orders.Setup(o => o.UpdateStatusAsync(YearMonth, OrderNumber, CancelledStatus, It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        details.Setup(d => d.IncreaseStockAsync(TicketId, Quantity, It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        // Redis restore fails — Java logs a warning but does not fail the request.
        stockCache.Setup(s => s.IncreaseStockCacheAsync(TicketId, Quantity, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(false);

        var sut = BuildSut(orders, domain, details, stockCache, locks);
        var result = await sut.CancelOrderAsync(UserId, OrderNumber);

        Assert.True(result);
    }

    // ============== Input validation ==============

    [Fact]
    public async Task CancelOrderAsync_throws_when_orderNumber_is_empty()
    {
        var orders = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        var details = new Mock<ITicketDetailRepository>(MockBehavior.Strict);
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var locks = new LockHarness(acquired: true);

        var sut = BuildSut(orders, domain, details, stockCache, locks);
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CancelOrderAsync(UserId, ""));
    }
}