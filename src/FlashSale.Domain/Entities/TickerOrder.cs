namespace FlashSale.Domain.Entities;

/// <summary>
/// TickerOrder entity — written to monthly shard table ticket_order_yyyyMM.
/// Mirrors Java com.xxxx.ddd.domain.model.entity.TickerOrder.
/// </summary>
public class TickerOrder
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int UserId { get; set; }
    public int TicketId { get; set; }
    public int Quantity { get; set; }
    public decimal TotalAmount { get; set; }
    public string TerminalId { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string? OrderNotes { get; set; }

    /// <summary>0=PENDING, 1=SUCCESS, 2=CANCELLED, 3=EXPIRED, 4=REFUNDED</summary>
    public int OrderStatus { get; set; }

    public DateTime UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}