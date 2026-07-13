namespace FlashSale.Domain.Enums;

/// <summary>
/// Order status — mirrors TickerOrder.orderStatus column.
/// 0=PENDING, 1=SUCCESS, 2=CANCELLED, 3=EXPIRED, 4=REFUNDED.
/// </summary>
public enum OrderStatus
{
    PENDING = 0,
    SUCCESS = 1,
    CANCELLED = 2,
    EXPIRED = 3,
    REFUNDED = 4
}