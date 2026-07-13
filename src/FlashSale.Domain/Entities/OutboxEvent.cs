namespace FlashSale.Domain.Entities;

/// <summary>
/// OutboxEvent entity — mirrors outbox_event table.
/// Used by transactional outbox pattern to ensure atomic DB write + Kafka publish.
/// </summary>
public class OutboxEvent
{
    public long Id { get; set; }

    /// <summary>Token của order — dùng để idempotency check phía consumer</summary>
    public string AggregateId { get; set; } = string.Empty;

    /// <summary>Loại event, ví dụ: ORDER_PLACED</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>JSON của PlaceOrderMQMessage</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>0=PENDING, 1=PUBLISHED</summary>
    public int Status { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}