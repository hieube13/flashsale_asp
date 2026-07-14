using FlashSale.Application.Services;
using FlashSale.Application.Services.Implementations;
using FlashSale.Contracts.Messages;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using FlashSale.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlashSale.UnitTests.Application;

public class OrderMqConsumerHandlerTests
{
    private readonly Mock<IIdempotencyKeyRepository> _idempotency = new(MockBehavior.Strict);
    private readonly Mock<ITicketDetailRepository> _details = new(MockBehavior.Strict);
    private readonly Mock<IStockOrderCacheService> _stockCache = new(MockBehavior.Strict);
    private readonly Mock<IOrderQueueRepository> _orderQueues = new(MockBehavior.Strict);
    private readonly Mock<IOrderDeductionDomainService> _deduction = new(MockBehavior.Strict);
    private readonly OrderMqConsumerHandlerImpl _sut;

    public OrderMqConsumerHandlerTests()
    {
        _sut = new OrderMqConsumerHandlerImpl(
            _idempotency.Object,
            _details.Object,
            _stockCache.Object,
            _orderQueues.Object,
            _deduction.Object,
            NullLogger<OrderMqConsumerHandlerImpl>.Instance);
    }

    private static PlaceOrderMqMessage Sample(long ticketId = 4, int quantity = 2, int userId = 5, long unitPrice = 10_000, string token = "MQ-test-token")
        => new(token, ticketId, quantity, userId, unitPrice, 1718246100123);

    [Fact]
    public async Task HappyPath_persists_order_and_flips_status_to_success()
    {
        var msg = Sample();

        _idempotency.Setup(s => s.TryInsertAsync(msg.Token, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _details.Setup(s => s.TryDecreaseStockAsync(msg.TicketId, msg.Quantity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _deduction.Setup(s => s.InsertOrder(It.IsAny<string>(), It.IsAny<TickerOrder>()));
        _orderQueues.Setup(s => s.UpdateStatusAsync(msg.Token, 1, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.ProcessAsync(msg);

        _idempotency.VerifyAll();
        _details.VerifyAll();
        _deduction.VerifyAll();
        _orderQueues.VerifyAll();
        _stockCache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task IdempotencyDuplicate_skips_all_side_effects()
    {
        var msg = Sample();

        _idempotency.Setup(s => s.TryInsertAsync(msg.Token, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await _sut.ProcessAsync(msg);

        _idempotency.VerifyAll();
        _details.VerifyNoOtherCalls();
        _stockCache.VerifyNoOtherCalls();
        _deduction.VerifyNoOtherCalls();
        _orderQueues.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OOS_compensates_redis_and_flips_status_to_failed()
    {
        var msg = Sample();

        _idempotency.Setup(s => s.TryInsertAsync(msg.Token, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _details.Setup(s => s.TryDecreaseStockAsync(msg.TicketId, msg.Quantity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _stockCache.Setup(s => s.IncreaseStockCacheAsync(msg.TicketId, msg.Quantity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _orderQueues.Setup(s => s.UpdateStatusAsync(msg.Token, 2, null, "Hết vé", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.ProcessAsync(msg);

        _idempotency.VerifyAll();
        _details.VerifyAll();
        _stockCache.VerifyAll();
        _orderQueues.VerifyAll();
        _deduction.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DapperInsertFailure_propagates_exception()
    {
        var msg = Sample();

        _idempotency.Setup(s => s.TryInsertAsync(msg.Token, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _details.Setup(s => s.TryDecreaseStockAsync(msg.TicketId, msg.Quantity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _deduction.Setup(s => s.InsertOrder(It.IsAny<string>(), It.IsAny<TickerOrder>()))
            .Throws(new InvalidOperationException("insert boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ProcessAsync(msg));
    }

    [Fact]
    public async Task StatusUpdateFailure_propagates_exception()
    {
        var msg = Sample();

        _idempotency.Setup(s => s.TryInsertAsync(msg.Token, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _details.Setup(s => s.TryDecreaseStockAsync(msg.TicketId, msg.Quantity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _deduction.Setup(s => s.InsertOrder(It.IsAny<string>(), It.IsAny<TickerOrder>()));
        _orderQueues.Setup(s => s.UpdateStatusAsync(msg.Token, 1, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("status boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ProcessAsync(msg));
    }

    [Fact]
    public async Task HappyPath_orderNumber_format_matches_MQ_userId_tsMillis()
    {
        var msg = Sample(userId: 42);

        _idempotency.Setup(s => s.TryInsertAsync(msg.Token, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _details.Setup(s => s.TryDecreaseStockAsync(msg.TicketId, msg.Quantity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        TickerOrder? captured = null;
        _deduction.Setup(s => s.InsertOrder(It.IsAny<string>(), It.IsAny<TickerOrder>()))
            .Callback<string, TickerOrder>((_, o) => captured = o);
        _orderQueues.Setup(s => s.UpdateStatusAsync(msg.Token, 1, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.ProcessAsync(msg);

        Assert.NotNull(captured);
        Assert.StartsWith("MQ-42-", captured!.OrderNumber);
        Assert.Matches(@"^MQ-42-\d+$", captured.OrderNumber);
    }

    [Fact]
    public async Task HappyPath_total_amount_is_unitPrice_times_quantity()
    {
        var msg = Sample(quantity: 3, unitPrice: 12_500);

        _idempotency.Setup(s => s.TryInsertAsync(msg.Token, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _details.Setup(s => s.TryDecreaseStockAsync(msg.TicketId, msg.Quantity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        TickerOrder? captured = null;
        _deduction.Setup(s => s.InsertOrder(It.IsAny<string>(), It.IsAny<TickerOrder>()))
            .Callback<string, TickerOrder>((_, o) => captured = o);
        _orderQueues.Setup(s => s.UpdateStatusAsync(msg.Token, 1, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.ProcessAsync(msg);

        Assert.NotNull(captured);
        Assert.Equal(12_500m * 3, captured!.TotalAmount);
    }

    [Fact]
    public async Task NullMessage_throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.ProcessAsync(null!));
    }

    [Fact]
    public async Task EmptyToken_throws()
    {
        var msg = new PlaceOrderMqMessage("", 4, 1, 1, 1, 1);
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ProcessAsync(msg));
    }
}