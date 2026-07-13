namespace FlashSale.Infrastructure.Cache;

/// <summary>
/// Redis abstraction — mirrors Java RedisInfrasService.
/// </summary>
public interface IRedisInfrasService
{
    Task SetStringAsync(string key, string value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task<string?> GetStringAsync(string key, CancellationToken ct = default);
    Task SetObjectAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task<T?> GetObjectAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task SetIntAsync(string key, int value, CancellationToken ct = default);
    Task<int> GetIntAsync(string key, CancellationToken ct = default);
    Task<int> IncrementAsync(string key, int delta, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}