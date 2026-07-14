using FlashSale.Application.Services;
using StackExchange.Redis;

namespace FlashSale.Infrastructure.Cache;

/// <summary>
/// StackExchange.Redis implementation of <see cref="IEmployeeBitSetService"/>.
/// Thin wrapper exposing the three BitSet primitives needed by
/// <see cref="FlashSale.Application.Services.Implementations.EmployeeCacheServiceImpl"/>
/// across the architecture boundary.
/// </summary>
public sealed class EmployeeBitSetService : IEmployeeBitSetService
{
    private readonly IConnectionMultiplexer _redis;

    public EmployeeBitSetService(IConnectionMultiplexer redis) => _redis = redis;

    private IDatabase Db => _redis.GetDatabase();

    public Task SetBitAsync(string key, long offset, bool value, CancellationToken ct = default)
        => Db.StringSetBitAsync(key, offset, value);

    public Task<bool> GetBitAsync(string key, long offset, CancellationToken ct = default)
        => Db.StringGetBitAsync(key, offset);

    public Task<long> BitCountAsync(string key, CancellationToken ct = default)
        => Db.StringBitCountAsync(key);
}
