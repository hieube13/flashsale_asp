using FlashSale.Domain.Services;

namespace FlashSale.Domain.Services.Implementations;

/// <summary>
/// Domain service for order deduction. TASK-012 ships only the year-month
/// parser; the rest of <see cref="IOrderDeductionDomainService"/> land in
/// TASK-013/014 as the order CAS / cancel slices bring their own methods.
/// </summary>
public sealed class OrderDeductionDomainService : IOrderDeductionDomainService
{
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
}
