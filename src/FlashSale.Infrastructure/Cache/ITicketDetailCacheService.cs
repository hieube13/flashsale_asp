using FlashSale.Infrastructure.DistributedLock;

namespace FlashSale.Infrastructure.Cache;

/// <summary>
/// Wrapper holding a TicketDetail snapshot with a monotonic version number
/// so consumers can detect stale reads. Mirrors Java TicketDetailCache.
/// </summary>
public sealed record TicketDetailCacheEntry(
    long Id,
    string Name,
    int StockInitial,
    int StockAvailable,
    decimal PriceOriginal,
    decimal PriceFlash,
    DateTime SaleStartTime,
    DateTime SaleEndTime,
    int Status,
    long ActivityId,
    long Version)
{
    public static TicketDetailCacheEntry From(
        FlashSale.Domain.Entities.TicketDetail detail, long version) =>
        new(detail.Id, detail.Name, detail.StockInitial, detail.StockAvailable,
            detail.PriceOriginal, detail.PriceFlash, detail.SaleStartTime, detail.SaleEndTime,
            detail.Status, detail.ActivityId, version);
}

/// <summary>
/// Two-tier cache for TicketDetail: in-process Memory + distributed Redis.
/// Mirrors Java TicketDetailCacheServiceRefactor (Guava local + Redis + Redisson lock).
/// </summary>
public interface ITicketDetailCacheService
{
    Task<TicketDetailCacheEntry?> GetAsync(long ticketId, long? version, CancellationToken ct = default);
    Task SetAsync(TicketDetailCacheEntry entry, CancellationToken ct = default);
    Task InvalidateAsync(long ticketId, CancellationToken ct = default);
    Task<bool> OrderByUserAsync(long ticketId, CancellationToken ct = default);
}
