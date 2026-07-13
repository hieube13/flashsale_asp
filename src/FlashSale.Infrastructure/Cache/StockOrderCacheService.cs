using FlashSale.Application.Services;

namespace FlashSale.Infrastructure.Cache;

/// <summary>
/// Stock cache stub — real Lua atomic decrement added in TASK-006/013.
/// For now provides simple Redis INCR/DECR semantics for early-stage smoke testing.
/// </summary>
public sealed class StockOrderCacheService : IStockOrderCacheService
{
    private readonly IRedisInfrasService _redis;

    public StockOrderCacheService(IRedisInfrasService redis) => _redis = redis;

    private static string StockKey(long ticketId) => $"PRO_TICKET:{ticketId}:stock_available";
    private static string PriceKey(long ticketId) => $"PRO_TICKET:{ticketId}:price_flash";

    public async Task<int> DecreaseStockCacheByLuaAsync(long ticketId, int quantity, CancellationToken ct = default)
    {
        var key = StockKey(ticketId);
        if (!await _redis.ExistsAsync(key, ct)) return -1;
        var current = await _redis.GetIntAsync(key, ct);
        if (current < quantity) return 0;
        await _redis.IncrementAsync(key, -quantity, ct);
        return 1;
    }

    public async Task<bool> IncreaseStockCacheAsync(long ticketId, int quantity, CancellationToken ct = default)
    {
        await _redis.IncrementAsync(StockKey(ticketId), quantity, ct);
        return true;
    }

    public Task<bool> AddStockAvailableToCacheAsync(long ticketId, CancellationToken ct = default)
        => Task.FromResult(false);

    public async Task<long> GetEffectivePriceAsync(long ticketId, CancellationToken ct = default)
    {
        var v = await _redis.GetStringAsync(PriceKey(ticketId), ct);
        return long.TryParse(v, out var p) ? p : 0;
    }
}