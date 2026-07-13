namespace FlashSale.Domain.Services;

/// <summary>
/// Domain service — encapsulates business invariants.
/// In Java, OrderDeductionDomainService has the dedup / cursor logic.
/// </summary>
public interface IOrderDeductionDomainService
{
    string ExtractYearMonth(string orderNumber);
}