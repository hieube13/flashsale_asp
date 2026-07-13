namespace FlashSale.Domain.Enums;

/// <summary>
/// Order queue status — mirrors order_queue.status column.
/// 0=PENDING, 1=SUCCESS, 2=FAILED.
/// </summary>
public enum OrderQueueStatus
{
    PENDING = 0,
    SUCCESS = 1,
    FAILED = 2
}