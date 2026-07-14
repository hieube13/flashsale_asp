using FlashSale.Domain.Entities;

namespace FlashSale.Domain.Services;

/// <summary>
/// Domain service — encapsulates business invariants.
/// In Java, OrderDeductionDomainService has the dedup / cursor logic.
/// </summary>
public interface IOrderDeductionDomainService
{
    string ExtractYearMonth(string orderNumber);

    /// <summary>
    /// Insert a TickerOrder into the monthly shard <c>ticket_order_{yearMonth}</c>.
    /// Mirrors Java <c>OrderDeductionDomainServiceImpl.insertOrder(yearMonth, order)</c>
    /// (called from <c>KafkaOrderConsumer.processOrder</c> line 77).
    /// </summary>
    void InsertOrder(string yearMonth, TickerOrder order);

    /// <summary>
    /// Stub entry point for the queued order path.
    /// Mirrors Java <c>mqPlaceOrderService.startOrderByUser</c> (line 16-24 of
    /// <c>MQPlaceOrderServiceImpl.java</c>), which is a stub that always returns
    /// <c>false</c>. This method exists as a seam so the domain layer does not
    /// reach into the Kafka/Redis infrastructure directly.
    /// </summary>
    Task<bool> StartOrderByUserAsync(long userId, long ticketId, int quantity, CancellationToken ct = default);
}