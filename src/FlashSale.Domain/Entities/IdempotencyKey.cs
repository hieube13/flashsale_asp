namespace FlashSale.Domain.Entities;

/// <summary>
/// IdempotencyKey entity — consumer gate chống Kafka retry duplicate.
/// Insert IGNORE pattern; same transaction as business logic.
/// </summary>
public class IdempotencyKey
{
    /// <summary>Unique token from PlaceOrderMQMessage.</summary>
    public string Token { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}