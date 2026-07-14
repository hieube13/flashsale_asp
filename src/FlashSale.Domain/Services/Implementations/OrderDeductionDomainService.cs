using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using FlashSale.Domain.Services;

namespace FlashSale.Domain.Services.Implementations;

/// <summary>
/// Domain service for order deduction. TASK-012 ships the year-month
/// parser (read slice), TASK-016 brings the write path
/// (<see cref="InsertOrder"/>) consumed by the OrderMQ Kafka handler.
/// </summary>
public sealed class OrderDeductionDomainService : IOrderDeductionDomainService
{
    private readonly ITickerOrderRepository _orders;

    public OrderDeductionDomainService(ITickerOrderRepository orders) => _orders = orders;

    /// <summary>
    /// Extract the yyyyMM shard name from an order_number like
    /// "OKX-SGN-7-42-1718246100123" — the LAST dash-separated segment is
    /// a unix-millis timestamp; we format its month as "yyyyMM".
    /// Preserved verbatim from Java
    /// (TicketOrderAppServiceImpl.extractYearMonthFromOrderNumber).
    /// </summary>
    public string ExtractYearMonth(string orderNumber)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new ArgumentException("orderNumber is required", nameof(orderNumber));

        var parts = orderNumber.Split('-');
        if (parts.Length < 2)
            throw new ArgumentException("Invalid order number format", nameof(orderNumber));

        var timestampStr = parts[^1];
        if (!long.TryParse(timestampStr, out var ts))
            throw new ArgumentException("Order number timestamp segment is not a long", nameof(orderNumber));

        var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(ts)
            .ToLocalTime()
            .DateTime;

        return dateTime.ToString("yyyyMM");
    }

    /// <summary>
    /// Insert a TickerOrder into the monthly shard <c>ticket_order_{yearMonth}</c>.
    /// Synchronous version (matches Java line 77 — called from Kafka listener
    /// which runs inside an <c>@Transactional</c> boundary). .NET equivalent: call
    /// inside the handler's ambient scope; the repo uses Dapper + the same EF
    /// connection indirectly. Async overload recommended for non-consumer paths.
    /// </summary>
    public void InsertOrder(string yearMonth, TickerOrder order)
    {
        // Java is sync; .NET repos expose Async-only. Run sync-wait at the
        // domain-layer seam so the handler signature stays async-friendly.
        _orders.InsertAsync(yearMonth, order, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Stub entry point for the queued order path.
    /// Mirrors Java <c>mqPlaceOrderService.startOrderByUser</c> which always
    /// returns <c>false</c> (<c>MQPlaceOrderTokenServiceImpl.submitOrderToQueued</c> line 26).
    /// This exists as a seam so the application layer does not reach into Kafka/Redis
    /// directly when implementing <c>DecreaseStockQueueAsync</c>.
    /// </summary>
    public Task<bool> StartOrderByUserAsync(long userId, long ticketId, int quantity, CancellationToken ct = default)
        => Task.FromResult(false);
}
