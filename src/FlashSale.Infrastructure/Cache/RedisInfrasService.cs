using FlashSale.Infrastructure.Cache;
using StackExchange.Redis;

namespace FlashSale.Infrastructure.Cache;

/// <summary>
/// StackExchange.Redis implementation of IRedisInfrasService.
/// Mirrors Java RedisInfrasServiceImpl.
/// </summary>
public sealed class RedisInfrasService : IRedisInfrasService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly System.Text.Json.JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    public RedisInfrasService(IConnectionMultiplexer redis) => _redis = redis;

    private IDatabase Db => _redis.GetDatabase();

    public async Task SetStringAsync(string key, string value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key)) return;
        await Db.StringSetAsync(key, value, expiry);
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken ct = default)
    {
        var v = await Db.StringGetAsync(key);
        return v.HasValue ? v.ToString() : null;
    }

    public async Task SetObjectAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key)) return;
        var json = System.Text.Json.JsonSerializer.Serialize(value, _json);
        await Db.StringSetAsync(key, json, expiry);
    }

    public async Task<T?> GetObjectAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        var v = await Db.StringGetAsync(key);
        if (!v.HasValue) return null;
        return System.Text.Json.JsonSerializer.Deserialize<T>(v.ToString(), _json);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key)) return;
        await Db.KeyDeleteAsync(key);
    }

    public async Task SetIntAsync(string key, int value, CancellationToken ct = default)
        => await Db.StringSetAsync(key, value);

    public async Task<int> GetIntAsync(string key, CancellationToken ct = default)
    {
        var v = await Db.StringGetAsync(key);
        return v.HasValue ? (int)v : 0;
    }

    public async Task<int> IncrementAsync(string key, int delta, CancellationToken ct = default)
        => (int)await Db.StringIncrementAsync(key, delta);

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => await Db.KeyExistsAsync(key);
}