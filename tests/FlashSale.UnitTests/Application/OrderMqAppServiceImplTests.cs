using System.Text.Json;
using FlashSale.Application.Services;
using FlashSale.Application.Services.Implementations;
using FlashSale.Contracts.Messages;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlashSale.UnitTests.Application;

/// <summary>
/// Unit tests for <see cref="OrderMqAppServiceImpl.PlaceOrderMqAsync"/> (TASK-015).
/// Covers the producer-side pipeline: Lua gate → price lookup → atomic outbox write
/// → compensation on failure.
/// </summary>
public class OrderMqAppServiceImplTests
{
    private const long TicketId = 4L;
    private const int Quantity = 2;
    private const long UnitPrice = 10000L;

    private static OrderMqAppServiceImpl Build(
        Mock<IStockOrderCacheService> stockCache,
        Mock<IOrderQueueRepository> orderQueues,
        Mock<IOrderMqTransactionService> txService,
        bool throwOnPersist = false)
    {
        if (throwOnPersist)
        {
            txService.Setup(t => t.PersistAsync(It.IsAny<OrderQueue>(), It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new InvalidOperationException("simulated tx failure"));
        }
        return new OrderMqAppServiceImpl(
            stockCache.Object,
            orderQueues.Object,
            txService.Object,
            NullLogger<OrderMqAppServiceImpl>.Instance);
    }

    // ============== Happy path ==============

    [Fact]
    public async Task PlaceOrderMqAsync_happy_path_returns_pending_queue_with_token()
    {
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var orderQueues = new Mock<IOrderQueueRepository>(MockBehavior.Strict);
        var txService = new Mock<IOrderMqTransactionService>(MockBehavior.Strict);

        stockCache.Setup(s => s.DecreaseStockCacheByLuaAsync(TicketId, Quantity, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(1);
        stockCache.Setup(s => s.GetEffectivePriceAsync(TicketId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(UnitPrice);
        OutboxEvent? capturedOutbox = null;
        OrderQueue? capturedQueue = null;
        txService.Setup(t => t.PersistAsync(It.IsAny<OrderQueue>(), It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
                 .Callback<OrderQueue, OutboxEvent, CancellationToken>((q, e, _) =>
                 {
                     capturedQueue = q;
                     capturedOutbox = e;
                 })
                 .Returns(Task.CompletedTask);

        var sut = Build(stockCache, orderQueues, txService);
        var result = await sut.PlaceOrderMqAsync(TicketId, Quantity);

        Assert.Equal(0, result.Status);
        Assert.StartsWith("MQ-", result.Token);
        Assert.Equal(TicketId, result.TicketId);
        Assert.Equal(Quantity, result.Quantity);
        Assert.NotNull(capturedQueue);
        Assert.NotNull(capturedOutbox);
        Assert.Equal(result.Token, capturedOutbox!.AggregateId);
        Assert.Equal("ORDER_PLACED", capturedOutbox.EventType);
        Assert.Equal(0, capturedOutbox.Status);
    }

    [Fact]
    public async Task PlaceOrderMqAsync_outbox_payload_contains_serialized_message_with_correct_unit_price()
    {
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var orderQueues = new Mock<IOrderQueueRepository>(MockBehavior.Strict);
        var txService = new Mock<IOrderMqTransactionService>(MockBehavior.Strict);

        stockCache.Setup(s => s.DecreaseStockCacheByLuaAsync(TicketId, Quantity, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(1);
        stockCache.Setup(s => s.GetEffectivePriceAsync(TicketId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(UnitPrice);
        OutboxEvent? capturedOutbox = null;
        txService.Setup(t => t.PersistAsync(It.IsAny<OrderQueue>(), It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
                 .Callback<OrderQueue, OutboxEvent, CancellationToken>((_, e, _) => capturedOutbox = e)
                 .Returns(Task.CompletedTask);

        var sut = Build(stockCache, orderQueues, txService);
        await sut.PlaceOrderMqAsync(TicketId, Quantity);

        Assert.NotNull(capturedOutbox);
        var payload = JsonSerializer.Deserialize<PlaceOrderMqMessage>(capturedOutbox!.Payload)!;
        Assert.Equal(TicketId, payload.TicketId);
        Assert.Equal(Quantity, payload.Quantity);
        Assert.Equal(UnitPrice, payload.UnitPrice);
        Assert.Equal(capturedOutbox.AggregateId, payload.Token);
    }

    // ============== Lua gate ==============

    [Fact]
    public async Task PlaceOrderMqAsync_redis_out_of_stock_returns_failed_queue_without_warming()
    {
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var orderQueues = new Mock<IOrderQueueRepository>(MockBehavior.Strict);
        var txService = new Mock<IOrderMqTransactionService>(MockBehavior.Strict);

        stockCache.Setup(s => s.DecreaseStockCacheByLuaAsync(TicketId, Quantity, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(0);

        var sut = Build(stockCache, orderQueues, txService);
        var result = await sut.PlaceOrderMqAsync(TicketId, Quantity);

        Assert.Equal(2, result.Status);
        Assert.Equal("OUT_OF_STOCK: Hết vé", result.Message);
        stockCache.Verify(s => s.AddStockAvailableToCacheAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        txService.Verify(t => t.PersistAsync(It.IsAny<OrderQueue>(), It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PlaceOrderMqAsync_cache_miss_warms_then_retries_lua()
    {
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var orderQueues = new Mock<IOrderQueueRepository>(MockBehavior.Strict);
        var txService = new Mock<IOrderMqTransactionService>(MockBehavior.Strict);

        var calls = 0;
        stockCache.Setup(s => s.DecreaseStockCacheByLuaAsync(TicketId, Quantity, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(() => (++calls == 1) ? -1 : 1);
        stockCache.Setup(s => s.AddStockAvailableToCacheAsync(TicketId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true);
        stockCache.Setup(s => s.GetEffectivePriceAsync(TicketId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(UnitPrice);
        txService.Setup(t => t.PersistAsync(It.IsAny<OrderQueue>(), It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var sut = Build(stockCache, orderQueues, txService);
        var result = await sut.PlaceOrderMqAsync(TicketId, Quantity);

        Assert.Equal(0, result.Status);
        Assert.Equal(2, calls);
        stockCache.Verify(s => s.AddStockAvailableToCacheAsync(TicketId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PlaceOrderMqAsync_cache_miss_warm_fails_returns_ticket_not_found()
    {
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var orderQueues = new Mock<IOrderQueueRepository>(MockBehavior.Strict);
        var txService = new Mock<IOrderMqTransactionService>(MockBehavior.Strict);

        stockCache.Setup(s => s.DecreaseStockCacheByLuaAsync(TicketId, Quantity, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(-1);
        stockCache.Setup(s => s.AddStockAvailableToCacheAsync(TicketId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(false);

        var sut = Build(stockCache, orderQueues, txService);
        var result = await sut.PlaceOrderMqAsync(TicketId, Quantity);

        Assert.Equal(2, result.Status);
        Assert.Equal("TICKET_NOT_FOUND: Không tìm thấy sự kiện", result.Message);
        stockCache.Verify(s => s.GetEffectivePriceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        txService.Verify(t => t.PersistAsync(It.IsAny<OrderQueue>(), It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ============== Price lookup + Redis compensation ==============

    [Fact]
    public async Task PlaceOrderMqAsync_price_not_found_compensates_redis_and_returns_failed()
    {
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var orderQueues = new Mock<IOrderQueueRepository>(MockBehavior.Strict);
        var txService = new Mock<IOrderMqTransactionService>(MockBehavior.Strict);

        stockCache.Setup(s => s.DecreaseStockCacheByLuaAsync(TicketId, Quantity, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(1);
        stockCache.Setup(s => s.GetEffectivePriceAsync(TicketId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(0L);
        // Compensation: IncreaseStockCacheAsync is called because Redis was decremented.
        stockCache.Setup(s => s.IncreaseStockCacheAsync(TicketId, Quantity, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true);

        var sut = Build(stockCache, orderQueues, txService);
        var result = await sut.PlaceOrderMqAsync(TicketId, Quantity);

        Assert.Equal(2, result.Status);
        Assert.Equal("PRICE_NOT_FOUND: Không thể xác định giá vé", result.Message);
        stockCache.Verify(s => s.IncreaseStockCacheAsync(TicketId, Quantity, It.IsAny<CancellationToken>()), Times.Once);
        txService.Verify(t => t.PersistAsync(It.IsAny<OrderQueue>(), It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ============== Tx failure → Redis compensation ==============

    [Fact]
    public async Task PlaceOrderMqAsync_tx_failure_compensates_redis_and_returns_internal_error()
    {
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var orderQueues = new Mock<IOrderQueueRepository>(MockBehavior.Strict);
        var txService = new Mock<IOrderMqTransactionService>(MockBehavior.Strict);

        stockCache.Setup(s => s.DecreaseStockCacheByLuaAsync(TicketId, Quantity, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(1);
        stockCache.Setup(s => s.GetEffectivePriceAsync(TicketId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(UnitPrice);
        stockCache.Setup(s => s.IncreaseStockCacheAsync(TicketId, Quantity, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true);
        txService.Setup(t => t.PersistAsync(It.IsAny<OrderQueue>(), It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("DB write failed"));

        var sut = Build(stockCache, orderQueues, txService, throwOnPersist: true);
        var result = await sut.PlaceOrderMqAsync(TicketId, Quantity);

        Assert.Equal(2, result.Status);
        Assert.Equal("INTERNAL_ERROR: Lỗi hệ thống, vui lòng thử lại", result.Message);
        stockCache.Verify(s => s.IncreaseStockCacheAsync(TicketId, Quantity, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ============== Input validation ==============

    [Fact]
    public async Task PlaceOrderMqAsync_invalid_quantity_returns_failed_without_lua_call()
    {
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var orderQueues = new Mock<IOrderQueueRepository>(MockBehavior.Strict);
        var txService = new Mock<IOrderMqTransactionService>(MockBehavior.Strict);

        var sut = Build(stockCache, orderQueues, txService);
        var result = await sut.PlaceOrderMqAsync(TicketId, 0);

        Assert.Equal(2, result.Status);
        Assert.Contains("INVALID_QUANTITY", result.Message);
        stockCache.Verify(s => s.DecreaseStockCacheByLuaAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ============== Token format ==============

    [Fact]
    public void GenerateToken_starts_with_MQ_dash_and_has_16_hex_chars()
    {
        var token = OrderMqAppServiceImpl.GenerateToken();
        Assert.StartsWith("MQ-", token);
        var hex = token["MQ-".Length..];
        Assert.Equal(16, hex.Length);
        // Hex only
        foreach (var c in hex)
        {
            Assert.True((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'),
                $"unexpected char in token: {c}");
        }
    }

    [Fact]
    public void GenerateToken_produces_unique_values()
    {
        var t1 = OrderMqAppServiceImpl.GenerateToken();
        var t2 = OrderMqAppServiceImpl.GenerateToken();
        Assert.NotEqual(t1, t2);
    }

    // ============== Get status ==============

    [Fact]
    public async Task GetOrderStatusAsync_delegates_to_repository()
    {
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var orderQueues = new Mock<IOrderQueueRepository>(MockBehavior.Strict);
        var txService = new Mock<IOrderMqTransactionService>(MockBehavior.Strict);

        var fake = new OrderQueue { Token = "MQ-abc123", Status = 1 };
        orderQueues.Setup(r => r.GetByTokenAsync("MQ-abc123", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(fake);

        var sut = Build(stockCache, orderQueues, txService);
        var result = await sut.GetOrderStatusAsync("MQ-abc123");

        Assert.NotNull(result);
        Assert.Equal(1, result!.Status);
    }

    [Fact]
    public async Task GetOrderStatusAsync_returns_null_when_not_found()
    {
        var stockCache = new Mock<IStockOrderCacheService>(MockBehavior.Strict);
        var orderQueues = new Mock<IOrderQueueRepository>(MockBehavior.Strict);
        var txService = new Mock<IOrderMqTransactionService>(MockBehavior.Strict);

        orderQueues.Setup(r => r.GetByTokenAsync("MQ-missing", It.IsAny<CancellationToken>()))
                   .ReturnsAsync((OrderQueue?)null);

        var sut = Build(stockCache, orderQueues, txService);
        var result = await sut.GetOrderStatusAsync("MQ-missing");

        Assert.Null(result);
    }
}