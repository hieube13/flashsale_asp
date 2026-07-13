namespace FlashSale.Infrastructure.Cache;

/// <summary>
/// Stock cache service — wraps Redis Lua atomic decrement.
/// Mirrors Java StockOrderCacheService.
///
/// Lua semantics:
///   -1 → cache miss (key not initialized)
///    0 → stock insufficient
///    1 → decrement success
/// </summary>
public interface IStockOrderCacheService
{
    /// <summary>Atomic decrease via Lua; returns -1 / 0 / 1.</summary>
    Task<int> DecreaseStockCacheByLuaAsync(long ticketId, int quantity, CancellationToken ct = default);

    /// <summary>Compensate Redis when DB rollback occurs.</summary>
    Task<bool> IncreaseStockCacheAsync(long ticketId, int quantity, CancellationToken ct = default);

    /// <summary>Warm-up cache from MySQL on first miss.</summary>
    Task<bool> AddStockAvailableToCacheAsync(long ticketId, CancellationToken ct = default);

    /// <summary>Effective price for current sale window (price_flash if sale open, else price_original).</summary>
    Task<long> GetEffectivePriceAsync(long ticketId, CancellationToken ct = default);
}