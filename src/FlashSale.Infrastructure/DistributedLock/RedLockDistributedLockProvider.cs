using FlashSale.Infrastructure.DistributedLock;

namespace FlashSale.Infrastructure.DistributedLock;

/// <summary>
/// RedLock.net implementation — concrete impl added in TASK-006.
/// This stub returns a no-op lock so DI graph compiles during scaffold phase.
/// </summary>
public sealed class RedLockDistributedLockProvider : IDistributedLockProvider
{
    public IDistributedLock GetLock(string key) => new NoOpLock();
    private sealed class NoOpLock : IDistributedLock
    {
        public bool IsAcquired => false;
        public Task<bool> TryAcquireAsync(TimeSpan expiry, TimeSpan? wait = null, CancellationToken ct = default) => Task.FromResult(true);
        public Task ReleaseAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}