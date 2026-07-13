using FlashSale.Application.Services;
using FlashSale.Application.Services.Implementations;
using FlashSale.Contracts.Dto;
using FlashSale.Domain.Repositories;
using FlashSale.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlashSale.UnitTests.Application;

public class TicketOrderAppServiceImplTests
{
    private static readonly DateTime OrderDate = new(2024, 7, 15, 10, 0, 0, DateTimeKind.Utc);

    private static object?[] SampleRow(int id = 1) => new object?[]
    {
        id,                  // 0  id
        7L,                  // 1  user_id
        11L,                 // 2  ticket_id
        2,                   // 3  quantity
        1,                   // 4  order_status (1 = SUCCESS)
        "OKX-SGN-7-42-1721035200000",
        20000L,
        "TERM-001",
        OrderDate,
        null,
        OrderDate,
        OrderDate,
    };

    private static Mock<ITickerOrderRepository> OrderRepo() => new(MockBehavior.Strict);
    private static Mock<IOrderDeductionDomainService> Domain() => new(MockBehavior.Strict);
    private static Mock<ITicketDetailRepository> DetailRepo() => new(MockBehavior.Strict);
    private static Mock<IStockOrderCacheService> StockCache() => new(MockBehavior.Strict);
    private static Mock<IDistributedLockProvider> LockProvider() => new(MockBehavior.Strict);

    private static TicketOrderAppServiceImpl Build(
        Mock<ITickerOrderRepository> orders,
        Mock<IOrderDeductionDomainService> domain,
        Mock<ITicketDetailRepository> details,
        Mock<IStockOrderCacheService> stockCache,
        Mock<IDistributedLockProvider>? lockProvider = null)
        => new(orders.Object, domain.Object, details.Object, stockCache.Object,
               (lockProvider ?? LockProvider()).Object,
               NullLogger<TicketOrderAppServiceImpl>.Instance);

    /// <summary>
    /// Helper to stand up a working lock provider that always grants
    /// the lock immediately (used by happy-path cancel tests).
    /// </summary>
    private static Mock<IDistributedLockProvider> GrantedLockProvider()
    {
        var provider = new Mock<IDistributedLockProvider>(MockBehavior.Strict);
        var handle = new Mock<IDistributedLock>(MockBehavior.Strict);
        handle.Setup(h => h.TryAcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        handle.Setup(h => h.ReleaseAsync(It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        provider.Setup(p => p.GetLock(It.IsAny<string>())).Returns(handle.Object);
        return provider;
    }

    // ============== TASK-012: read slice ==============

    [Fact]
    public async Task FindAllAsync_maps_rows_to_dtos()
    {
        var orders = OrderRepo();
        var domain = Domain();
        orders.Setup(r => r.FindAllAsync("202407", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<object?[]> { SampleRow(1), SampleRow(2) });

        var sut = Build(orders, domain, DetailRepo(), StockCache());

        var result = await sut.FindAllAsync("202407");

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(7, result[0].UserId);
        Assert.Equal(11, result[0].TicketId);
        Assert.Equal(2, result[0].Quantity);
        Assert.Equal(1, result[0].OrderStatus);
        Assert.Equal(20000m, result[0].TotalAmount);
        Assert.Equal("TERM-001", result[0].TerminalId);
        Assert.Equal(OrderDate, result[0].OrderDate);
        orders.VerifyAll();
    }

    [Fact]
    public async Task FindPageAsync_caps_limit_to_safe_max()
    {
        var orders = OrderRepo();
        var domain = Domain();
        orders.Setup(r => r.FindPageAsync("202407", 0L, 200, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<object?[]>());

        var sut = Build(orders, domain, DetailRepo(), StockCache());

        var result = await sut.FindPageAsync("202407", 0L, 999999);

        Assert.False(result.HasMore);
        Assert.Null(result.NextCursor);
        orders.VerifyAll();
    }

    [Fact]
    public async Task FindPageAsync_returns_nextCursor_and_hasMore_when_full_page()
    {
        var orders = OrderRepo();
        var domain = Domain();
        orders.Setup(r => r.FindPageAsync("202407", 100L, 2, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<object?[]> { SampleRow(99), SampleRow(98) });

        var sut = Build(orders, domain, DetailRepo(), StockCache());

        var result = await sut.FindPageAsync("202407", 100L, 2);

        Assert.True(result.HasMore);
        Assert.Equal(98, result.NextCursor);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task FindByOrderNumberAsync_uses_yearMonth_from_domain_then_returns_dto()
    {
        var orders = OrderRepo();
        var domain = Domain();
        const string on = "OKX-SGN-7-42-1721035200000";
        domain.Setup(d => d.ExtractYearMonth(on)).Returns("202407");
        orders.Setup(r => r.FindByOrderNumberAsync("202407", on, It.IsAny<CancellationToken>()))
              .ReturnsAsync(SampleRow(42));

        var sut = Build(orders, domain, DetailRepo(), StockCache());

        var result = await sut.FindByOrderNumberAsync("000000", on);

        Assert.NotNull(result);
        Assert.Equal(42, result!.Id);
        Assert.Equal("OKX-SGN-7-42-1721035200000", result.OrderNumber);
    }

    [Fact]
    public async Task FindByOrderNumberAsync_returns_null_when_repo_returns_null()
    {
        var orders = OrderRepo();
        var domain = Domain();
        const string on = "OKX-SGN-7-42-1721035200000";
        domain.Setup(d => d.ExtractYearMonth(on)).Returns("202407");
        orders.Setup(r => r.FindByOrderNumberAsync("202407", on, It.IsAny<CancellationToken>()))
              .ReturnsAsync((object?[]?)null);

        var sut = Build(orders, domain, DetailRepo(), StockCache());

        var result = await sut.FindByOrderNumberAsync("000000", on);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindByOrderNumberAsync_throws_ArgumentException_for_malformed_order_number()
    {
        var orders = OrderRepo();
        var domain = Domain();
        domain.Setup(d => d.ExtractYearMonth("INVALID")).Throws<ArgumentException>();

        var sut = Build(orders, domain, DetailRepo(), StockCache());

        await Assert.ThrowsAsync<ArgumentException>(() => sut.FindByOrderNumberAsync("000000", "INVALID"));
    }

    // ============== TASK-013: CAS slice ==============

    [Fact]
    public async Task PlaceOrderCasAsync_returns_OUT_OF_STOCK_when_redis_says_insufficient()
    {
        var orders = OrderRepo();
        var domain = Domain();
        var details = DetailRepo();
        var stock = StockCache();
        stock.Setup(s => s.DecreaseStockCacheByLuaAsync(11L, 2, It.IsAny<CancellationToken>()))
             .ReturnsAsync(0); // 0 = insufficient
        var sut = Build(orders, domain, details, stock);

        var resp = await sut.PlaceOrderCasAsync(11L, 2);

        Assert.False(resp.Success);
        Assert.Equal("OUT_OF_STOCK", resp.Code);
        stock.Verify(s => s.IncreaseStockCacheAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        orders.Verify(r => r.InsertAsync(It.IsAny<string>(), It.IsAny<FlashSale.Domain.Entities.TickerOrder>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PlaceOrderCasAsync_warms_cache_then_succeeds_when_cache_miss()
    {
        var orders = OrderRepo();
        var domain = Domain();
        var details = DetailRepo();
        var stock = StockCache();
        stock.Setup(s => s.DecreaseStockCacheByLuaAsync(11L, 1, It.IsAny<CancellationToken>()))
             .ReturnsAsync(-1);                       // miss
        stock.Setup(s => s.AddStockAvailableToCacheAsync(11L, It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);                     // warm OK
        stock.Setup(s => s.DecreaseStockCacheByLuaAsync(11L, 1, It.IsAny<CancellationToken>())) // retry
             .ReturnsAsync(-1);
        stock.Setup(s => s.DecreaseStockCacheByLuaAsync(11L, 1, It.IsAny<CancellationToken>()))
             .ReturnsAsync(1);
        // stub the second call by sequential setups
        var seq = stock.SetupSequence(s => s.DecreaseStockCacheByLuaAsync(11L, 1, It.IsAny<CancellationToken>()));
        seq.ReturnsAsync(-1);
        seq.ReturnsAsync(1);
        stock.Setup(s => s.GetEffectivePriceAsync(11L, It.IsAny<CancellationToken>())).ReturnsAsync(10000L);
        details.Setup(d => d.GetStockAvailableAsync(11L, It.IsAny<CancellationToken>())).ReturnsAsync(5);
        details.Setup(d => d.TryDecreaseStockAsync(11L, 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        orders.Setup(r => r.InsertAsync(It.IsAny<string>(),
            It.IsAny<FlashSale.Domain.Entities.TickerOrder>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut = Build(orders, domain, details, stock);

        var resp = await sut.PlaceOrderCasAsync(11L, 1);

        Assert.True(resp.Success);
        Assert.NotNull(resp.OrderNumber);
        Assert.StartsWith("OKX-SGN-", resp.OrderNumber);
    }

    [Fact]
    public async Task PlaceOrderCasAsync_returns_TICKET_NOT_FOUND_when_warmup_returns_false()
    {
        var orders = OrderRepo();
        var domain = Domain();
        var details = DetailRepo();
        var stock = StockCache();
        stock.Setup(s => s.DecreaseStockCacheByLuaAsync(11L, 1, It.IsAny<CancellationToken>())).ReturnsAsync(-1);
        stock.Setup(s => s.AddStockAvailableToCacheAsync(11L, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var sut = Build(orders, domain, details, stock);

        var resp = await sut.PlaceOrderCasAsync(11L, 1);

        Assert.False(resp.Success);
        Assert.Equal("TICKET_NOT_FOUND", resp.Code);
    }

    [Fact]
    public async Task PlaceOrderCasAsync_rolls_back_redis_when_db_safety_net_fails()
    {
        var orders = OrderRepo();
        var domain = Domain();
        var details = DetailRepo();
        var stock = StockCache();
        var seq = stock.SetupSequence(s => s.DecreaseStockCacheByLuaAsync(11L, 1, It.IsAny<CancellationToken>()));
        seq.ReturnsAsync(1);
        details.Setup(d => d.GetStockAvailableAsync(11L, It.IsAny<CancellationToken>())).ReturnsAsync(5);
        details.Setup(d => d.TryDecreaseStockAsync(11L, 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        stock.Setup(s => s.IncreaseStockCacheAsync(11L, 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var sut = Build(orders, domain, details, stock);

        var resp = await sut.PlaceOrderCasAsync(11L, 1);

        Assert.False(resp.Success);
        Assert.Equal("STOCK_CONFLICT", resp.Code);
        stock.Verify(s => s.IncreaseStockCacheAsync(11L, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PlaceOrderCasAsync_returns_PRICE_NOT_FOUND_and_rolls_back_redis()
    {
        var orders = OrderRepo();
        var domain = Domain();
        var details = DetailRepo();
        var stock = StockCache();
        var seq = stock.SetupSequence(s => s.DecreaseStockCacheByLuaAsync(11L, 1, It.IsAny<CancellationToken>()));
        seq.ReturnsAsync(1);
        details.Setup(d => d.GetStockAvailableAsync(11L, It.IsAny<CancellationToken>())).ReturnsAsync(5);
        details.Setup(d => d.TryDecreaseStockAsync(11L, 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        stock.Setup(s => s.GetEffectivePriceAsync(11L, It.IsAny<CancellationToken>())).ReturnsAsync(0L);
        stock.Setup(s => s.IncreaseStockCacheAsync(11L, 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var sut = Build(orders, domain, details, stock);

        var resp = await sut.PlaceOrderCasAsync(11L, 1);

        Assert.False(resp.Success);
        Assert.Equal("PRICE_NOT_FOUND", resp.Code);
        stock.Verify(s => s.IncreaseStockCacheAsync(11L, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PlaceOrderCasAsync_happy_path_inserts_order_row()
    {
        var orders = OrderRepo();
        var domain = Domain();
        var details = DetailRepo();
        var stock = StockCache();
        var seq = stock.SetupSequence(s => s.DecreaseStockCacheByLuaAsync(11L, 1, It.IsAny<CancellationToken>()));
        seq.ReturnsAsync(1);
        details.Setup(d => d.GetStockAvailableAsync(11L, It.IsAny<CancellationToken>())).ReturnsAsync(5);
        details.Setup(d => d.TryDecreaseStockAsync(11L, 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        stock.Setup(s => s.GetEffectivePriceAsync(11L, It.IsAny<CancellationToken>())).ReturnsAsync(15000L);
        FlashSale.Domain.Entities.TickerOrder? captured = null;
        orders.Setup(r => r.InsertAsync(It.IsAny<string>(),
            It.IsAny<FlashSale.Domain.Entities.TickerOrder>(), It.IsAny<CancellationToken>()))
              .Callback<string, FlashSale.Domain.Entities.TickerOrder, CancellationToken>((_, o, __) => captured = o)
              .Returns(Task.CompletedTask);

        var sut = Build(orders, domain, details, stock);

        var resp = await sut.PlaceOrderCasAsync(11L, 1);

        Assert.True(resp.Success);
        Assert.NotNull(resp.OrderNumber);
        Assert.NotNull(captured);
        Assert.Equal(11, captured!.TicketId);
        Assert.Equal(1, captured.Quantity);
        Assert.Equal(15000m, captured.TotalAmount);
        Assert.Equal("OKX-SGN", captured.TerminalId);
        Assert.Equal(0, captured.OrderStatus);
        Assert.Equal("Order -> Pending", captured.OrderNotes);
        // userId is random 1..9 (Java quirk preserved)
        Assert.InRange(captured.UserId, 1, 9);
    }

    [Fact]
    public async Task PlaceOrderCasAsync_invalid_quantity_returns_failed_without_calling_repo()
    {
        var orders = OrderRepo();
        var domain = Domain();
        var details = DetailRepo();
        var stock = StockCache();
        var sut = Build(orders, domain, details, stock);

        var resp = await sut.PlaceOrderCasAsync(11L, 0);

        Assert.False(resp.Success);
        Assert.Equal("INVALID_QUANTITY", resp.Code);
        stock.Verify(s => s.DecreaseStockCacheByLuaAsync(It.IsAny<long>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DecreaseStockLevel3CasAsync_returns_false_when_redis_insufficient_and_does_not_touch_db()
    {
        var orders = OrderRepo();
        var domain = Domain();
        var details = DetailRepo();
        var stock = StockCache();
        stock.Setup(s => s.DecreaseStockCacheByLuaAsync(11L, 2, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var sut = Build(orders, domain, details, stock);

        var ok = await sut.DecreaseStockLevel3CasAsync(11L, 2);

        Assert.False(ok);
        details.Verify(d => d.TryDecreaseStockAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        orders.Verify(r => r.InsertAsync(It.IsAny<string>(), It.IsAny<FlashSale.Domain.Entities.TickerOrder>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DecreaseStockLevel3CasAsync_happy_path_returns_true_and_inserts_order()
    {
        var orders = OrderRepo();
        var domain = Domain();
        var details = DetailRepo();
        var stock = StockCache();
        var seq = stock.SetupSequence(s => s.DecreaseStockCacheByLuaAsync(11L, 2, It.IsAny<CancellationToken>()));
        seq.ReturnsAsync(1);
        details.Setup(d => d.GetStockAvailableAsync(11L, It.IsAny<CancellationToken>())).ReturnsAsync(10);
        details.Setup(d => d.TryDecreaseStockAsync(11L, 2, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        stock.Setup(s => s.GetEffectivePriceAsync(11L, It.IsAny<CancellationToken>())).ReturnsAsync(12000L);
        orders.Setup(r => r.InsertAsync(It.IsAny<string>(),
            It.IsAny<FlashSale.Domain.Entities.TickerOrder>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut = Build(orders, domain, details, stock);

        var ok = await sut.DecreaseStockLevel3CasAsync(11L, 2);

        Assert.True(ok);
        orders.Verify(r => r.InsertAsync(It.IsAny<string>(),
            It.IsAny<FlashSale.Domain.Entities.TickerOrder>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStockAvailableAsync_returns_value_from_detail_repo()
    {
        var orders = OrderRepo();
        var domain = Domain();
        var details = DetailRepo();
        var stock = StockCache();
        details.Setup(d => d.GetStockAvailableAsync(11L, It.IsAny<CancellationToken>())).ReturnsAsync(42);
        var sut = Build(orders, domain, details, stock);

        var n = await sut.GetStockAvailableAsync(11L);

        Assert.Equal(42, n);
    }
}