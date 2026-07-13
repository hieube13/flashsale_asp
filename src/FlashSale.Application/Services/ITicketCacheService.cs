namespace FlashSale.Application.Services;

/// <summary>
/// Snapshot of a Ticket row stored in distributed cache.
/// Mirrors the inline cache class in Java TicketAppServiceImpl.
/// </summary>
public sealed record TicketCacheSnapshot(
    long Id,
    string Name,
    string? Description,
    DateTime StartTime,
    DateTime EndTime,
    int Status);

/// <summary>
/// Per-ticket cache abstraction. Mirrors Java TicketAppServiceImpl.cacheTicket()
/// + evictTicketCache() — Redis-only, no local tier.
/// </summary>
public interface ITicketCacheService
{
    Task SetAsync(long ticketId, TicketCacheSnapshot snapshot, CancellationToken ct = default);
    Task EvictAsync(long ticketId, CancellationToken ct = default);
}
