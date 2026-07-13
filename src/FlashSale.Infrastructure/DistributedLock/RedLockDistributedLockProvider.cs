using FlashSale.Application.Services;
using Microsoft.Extensions.Logging;
using RedLockNet;

namespace FlashSale.Infrastructure.DistributedLock;

/// <summary>
/// RedLock.net-backed implementation of <see cref="IDistributedLockProvider"/>.
/// <para>
/// Mirrors Java <c>RedisDistributedLocker</c> (Redisson-based) used by
/// <c>TicketOrderAppServiceImpl.cancelOrder</c> in Java lines 439-511, where
/// the lock key is <c>"LOCK:CANCEL_ORDER:" + orderNumber</c> with a 5 s
/// expiry and a 1 s wait timeout.
/// </para>
/// <para>
/// The .NET equivalent of <c>tryLock(1, 5, TimeUnit.SECONDS)</c> is
/// <c>await lock.TryAcquireAsync(expiry: 5 s, wait: 1 s)</c>.
/// </para>
/// </summary>
public sealed class RedLockDistributedLockProvider : IDistributedLockProvider
{
    private readonly IDistributedLockFactory _factory;
    private readonly ILogger<RedLockDistributedLockProvider> _log;

    public RedLockDistributedLockProvider(IDistributedLockFactory factory, ILogger<RedLockDistributedLockProvider> log)
    {
        _factory = factory;
        _log = log;
    }

    public IDistributedLock GetLock(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("lock key is required", nameof(key));
        return new RedLockNetLock(_factory, key, _log);
    }

    private sealed class RedLockNetLock : IDistributedLock
    {
        private readonly IDistributedLockFactory _factory;
        private readonly string _resource;
        private readonly ILogger _log;
        private IRedLock? _handle;
        private bool _isAcquired;

        public RedLockNetLock(IDistributedLockFactory factory, string resource, ILogger log)
        {
            _factory = factory;
            _resource = resource;
            _log = log;
        }

        public bool IsAcquired => _isAcquired;

        public async Task<bool> TryAcquireAsync(TimeSpan expiry, TimeSpan? wait = null, CancellationToken ct = default)
        {
            // Map Java tryLock(waitSec, expirySec) → RedLock.net blocking
            // CreateLockAsync(resource, expiry, wait, retry). The library already
            // polls until the wait budget elapses; retry=200 ms keeps the loop
            // snappy without hammering Redis.
            var waitTime = wait ?? TimeSpan.Zero;
            var retry = TimeSpan.FromMilliseconds(200);
            if (waitTime == TimeSpan.Zero)
            {
                // Non-blocking single attempt
                _handle = await _factory.CreateLockAsync(_resource, expiry);
            }
            else
            {
                _handle = await _factory.CreateLockAsync(_resource, expiry, waitTime, retry, ct);
            }
            _isAcquired = _handle.IsAcquired;
            if (_isAcquired)
                _log.LogDebug("RedLock acquired resource={Resource} expiryMs={Expiry} waitMs={Wait}",
                    _resource, expiry.TotalMilliseconds, waitTime.TotalMilliseconds);
            else
                _log.LogDebug("RedLock NOT acquired resource={Resource} (lock busy or quorum lost)", _resource);
            return _isAcquired;
        }

        public Task ReleaseAsync(CancellationToken ct = default)
        {
            if (_handle is null) return Task.CompletedTask;
            try
            {
                _handle.Dispose();
                _log.LogDebug("RedLock released resource={Resource}", _resource);
            }
            catch (Exception ex)
            {
                // RedLock expiry removes the key from Redis automatically —
                // Dispose failures (typically a network blip during release)
                // are not fatal. Log and swallow.
                _log.LogWarning(ex, "RedLock release failed (key will auto-expire): {Resource}", _resource);
            }
            finally
            {
                _handle = null;
                _isAcquired = false;
            }
            return Task.CompletedTask;
        }
    }
}