namespace FlashSale.Application.Services;

/// <summary>
/// Distributed lock abstraction — wraps RedLock.net / Redisson equivalent.
/// Mirrors Java RedisDistributedLocker (Redisson-based).
/// </summary>
public interface IDistributedLock
{
    Task<bool> TryAcquireAsync(TimeSpan expiry, TimeSpan? wait = null, CancellationToken ct = default);
    Task ReleaseAsync(CancellationToken ct = default);
    bool IsAcquired { get; }
}

public interface IDistributedLockProvider
{
    IDistributedLock GetLock(string key);
}