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
}