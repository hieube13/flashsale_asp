namespace FlashSale.Domain.Enums;

/// <summary>
/// Outbox event status — mirrors outbox_event.status column.
/// 0=PENDING, 1=PUBLISHED.
/// </summary>
public enum OutboxStatus
{
    PENDING = 0,
    PUBLISHED = 1
}