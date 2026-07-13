using FlashSale.Application.Services;
using FlashSale.Domain.Repositories;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FlashSale.Infrastructure.Cache;

/// <summary>
/// Stock cache service backed by Redis Lua atomic decrement.
///
/// Mirrors Java <c>StockOrderCacheService</c> contract:
///   <c>decreaseStockCacheByLUA</c> returns:
///     -1 → cache miss (key not initialized — caller must warm then retry)
///      0 → stock insufficient
///      1 → decrement success
///
/// The Lua script wraps EXISTS / GET / DECRBY into one round-trip so the
/// gate stays atomic under contention. See <c>scripts/decrement_stock.lua</c>
/// for the exact script. We also keep a fallback path for environments
/// where the Redis EVALSHA cache is invalidated (Redis restart) by loading
/// from the embedded string when NOSCRIPT is returned.
/// </summary>
public sealed class StockOrderCacheService : IStockOrderCacheService
{
    // KEYS[1] = stock key   (PRO_TICKET:{id}:stock_available)
    // ARGV[1] = quantity to decrement
    //
    // Returns the new stock value after decrement, or:
    //   -1  if the key does not exist (cache miss → caller warms + retries)
    //   -2  if the existing stock is below the requested quantity
    private const string DecrementScript = @"
local cur = redis.call('GET', KEYS[1])
if cur == false then return -1 end
local n = tonumber(cur)
local q = tonumber(ARGV[1])
if n < q then return -2 end
return redis.call('DECRBY', KEYS[1], q)
";

    private readonly IConnectionMultiplexer _redis;
    private readonly IRedisInfrasService _redisHelper;
    private readonly ITicketDetailRepository _details;
    private readonly ILogger<StockOrderCacheService> _log;

    public StockOrderCacheService(
        IConnectionMultiplexer redis,
        IRedisInfrasService redisHelper,
        ITicketDetailRepository details,
        ILogger<StockOrderCacheService> log)
    {
        _redis = redis;
        _redisHelper = redisHelper;
        _details = details;
        _log = log;
    }

    private static string StockKey(long ticketId) => $"PRO_TICKET:{ticketId}:stock_available";
    private static string PriceKey(long ticketId) => $"PRO_TICKET:{ticketId}:price_flash";

    public async Task<int> DecreaseStockCacheByLuaAsync(long ticketId, int quantity, CancellationToken ct = default)
    {
        if (quantity <= 0) return 0;
        var db = _redis.GetDatabase();
        var key = StockKey(ticketId);

        try
        {
            var raw = await db.ScriptEvaluateAsync(DecrementScript,
                new RedisKey[] { key },
                new RedisValue[] { quantity });
            var v = (long)raw;
            if (v == -1) return -1;          // cache miss
            if (v == -2) return 0;           // insufficient
            return 1;                        // success
        }
        catch (RedisServerException ex) when (ex.Message.Contains("NOSCRIPT", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogWarning("Lua script cache miss for {Key}; retrying", key);
            return await DecreaseStockCacheByLuaAsync(ticketId, quantity, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DecreaseStockCacheByLUA failed for ticket {TicketId}", ticketId);
            throw;
        }
    }

    public async Task<bool> IncreaseStockCacheAsync(long ticketId, int quantity, CancellationToken ct = default)
    {
        if (quantity <= 0) return true;
        var db = _redis.GetDatabase();
        // Atomic INCRBY — counter never goes below 0 because we only call this
        // to compensate failed safety-net writes, never to credit fresh stock.
        var newVal = await db.StringIncrementAsync(StockKey(ticketId), quantity);
        _log.LogInformation("IncreaseStockCache ticket={TicketId} qty={Qty} → newVal={Val}", ticketId, quantity, newVal);
        return true;
    }

    /// <summary>
    /// Warm-up: pull <c>stock_available</c> + <c>price_flash</c> from MySQL
    /// for the first ticket_item row of the given ticket and write to Redis.
    /// Returns false if no ticket_item is found — caller treats as TICKET_NOT_FOUND.
    /// </summary>
    public async Task<bool> AddStockAvailableToCacheAsync(long ticketId, CancellationToken ct = default)
    {
        var details = await _details.FindByActivityIdAsync(ticketId, ct);
        var first = details.FirstOrDefault();
        if (first is null)
        {
            _log.LogWarning("AddStockAvailableToCache: no ticket_item for ticketId={TicketId}", ticketId);
            return false;
        }
        await _redisHelper.SetIntAsync(StockKey(ticketId), first.StockAvailable, ct);
        await _redisHelper.SetStringAsync(PriceKey(ticketId), first.PriceFlash.ToString());
        _log.LogInformation("Warmed Redis for ticket={TicketId} stock={Stock} priceFlash={Price}",
            ticketId, first.StockAvailable, first.PriceFlash);
        return true;
    }

    public async Task<long> GetEffectivePriceAsync(long ticketId, CancellationToken ct = default)
    {
        var v = await _redisHelper.GetStringAsync(PriceKey(ticketId), ct);
        if (long.TryParse(v, out var p) && p > 0) return p;
        // Cache miss fallback: pull from MySQL and re-prime
        var details = await _details.FindByActivityIdAsync(ticketId, ct);
        var first = details.FirstOrDefault();
        if (first is null) return 0;
        var price = (long)first.PriceFlash;
        await _redisHelper.SetStringAsync(PriceKey(ticketId), price.ToString());
        return price;
    }
}