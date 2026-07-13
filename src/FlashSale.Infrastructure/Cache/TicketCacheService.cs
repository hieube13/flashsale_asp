using FlashSale.Application.Services;

namespace FlashSale.Infrastructure.Cache;

/// <summary>
/// Concrete Redis-only implementation of <see cref="Application.Services.ITicketCacheService"/>.
/// Mirrors Java TicketAppServiceImpl.cacheTicket.
/// </summary>
public sealed class TicketCacheService : ITicketCacheService
{
    private const string Prefix = "PRO_TICKET:";
    private readonly IRedisInfrasService _redis;

    public TicketCacheService(IRedisInfrasService redis) => _redis = redis;

    public async Task SetAsync(long ticketId, TicketCacheSnapshot snapshot, CancellationToken ct = default)
    {
        if (ticketId <= 0) return;
        try
        {
            await _redis.SetObjectAsync(Prefix + ticketId, snapshot,
                expiry: TimeSpan.FromHours(1), ct);
        }
        catch
        {
            // Cache failure must not block business flow (mirrors Java behaviour).
        }
    }

    public async Task EvictAsync(long ticketId, CancellationToken ct = default)
    {
        try { await _redis.DeleteAsync(Prefix + ticketId, ct); }
        catch { /* best effort */ }
    }
}
