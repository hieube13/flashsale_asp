namespace FlashSale.Domain.Entities;

/// <summary>
/// PaymentTransaction entity — mirrors payment_transaction table.
/// Payment status: 0=INIT, 1=IN_PROGRESS, 2=SUCCESS, 3=FAILED.
/// </summary>
public class PaymentTransaction
{
    public long Id { get; set; }
    public string PaymentId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public int UserId { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>0=INIT, 1=IN_PROGRESS, 2=SUCCESS, 3=FAILED</summary>
    public int PaymentStatus { get; set; }

    public string? GatewayTransactionId { get; set; }
    public string? PaymentUrl { get; set; }

    public DateTime UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}