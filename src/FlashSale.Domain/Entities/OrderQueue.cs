namespace FlashSale.Domain.Entities;

/// <summary>
/// OrderQueue entity — mirrors order_queue table.
/// Holds token (unique), ticketId, quantity, userId, status, orderNumber, message.
/// </summary>
public class OrderQueue
{
    public long Id { get; set; }

    /// <summary>Unique token, format: MQ-uuid16chars</summary>
    public string Token { get; set; } = string.Empty;

    public int TicketId { get; set; }
    public int Quantity { get; set; }
    public int UserId { get; set; }

    /// <summary>0=PENDING, 1=SUCCESS, 2=FAILED</summary>
    public int Status { get; set; }

    public string? OrderNumber { get; set; }
    public string? Message { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}