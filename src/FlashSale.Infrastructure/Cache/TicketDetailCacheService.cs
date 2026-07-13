using FlashSale.Domain.Repositories;
using FlashSale.Infrastructure.DistributedLock;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Cache;

/// <summary>
/// Two-tier cache for TicketDetail — Memory (L1) + Redis (L2) + MySQL (L3).
/// Implements <see cref="FlashSale.Application.Services.ITicketDetailCacheService"/>.
/// Mirrors Java TicketDetailCacheServiceRefactor.
/// </summary>
public sealed class TicketDetailCacheService : FlashSale.Application.Services.ITicketDetailCacheService
{
    private readonly IRedisInfrasService _redis;
    private readonly IDistributedLockProvider _locks;
    private readonly ITicketDetailRepository _repo;
    private readonly IMemoryCache _local;
    private readonly ILogger<TicketDetailCacheService> _log;

    private static readonly TimeSpan LocalTtl = TimeSpan.FromMinutes(5);
    private const string KeyPrefix = "PRO_TICKET:ITEM:";
    private const string LockPrefix = "PRO_LOCK_KEY_ITEM";

    public TicketDetailCacheService(
        IRedisInfrasService redis,
        IDistributedLockProvider locks,
        ITicketDetailRepository repo,
        IMemoryCache local,
        ILogger<TicketDetailCacheService> log)
    {
        _redis = redis;
        _locks = locks;
        _repo = repo;
        _local = local;
        _log = log;
    }

    private static string Key(long id) => KeyPrefix + id;
    private static string LockKey(long id) => LockPrefix + id;

    public async Task<FlashSale.Application.Services.TicketDetailCacheEntry?> GetAsync(long ticketId, long? version, CancellationToken ct = default)
    {
        if (ticketId <= 0) return null;

        var local = _local.Get<FlashSale.Application.Services.TicketDetailCacheEntry>(ticketId);
        if (local is not null)
        {
            _log.LogDebug("L1 HIT: ticketId={Id} version={V}", ticketId, local.Version);
            if (version is null || version <= local.Version) return local;
        }

        var distributed = await _redis.GetObjectAsync<FlashSale.Application.Services.TicketDetailCacheEntry>(Key(ticketId), ct);
        if (distributed is not null)
        {
            _local.Set(ticketId, distributed, LocalTtl);
            _log.LogDebug("L2 HIT: ticketId={Id} stockAvail={S}", ticketId, distributed.StockAvailable);
            return distributed;
        }

        var distLock = _locks.GetLock(LockKey(ticketId));
        if (!await distLock.TryAcquireAsync(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), ct))
            return distributed ?? local;

        try
        {
            distributed = await _redis.GetObjectAsync<FlashSale.Application.Services.TicketDetailCacheEntry>(Key(ticketId), ct);
            if (distributed is not null) return distributed;

            var detail = await _repo.GetByIdAsync(ticketId, ct);
            if (detail is null) return null;

            var entry = FlashSale.Application.Services.TicketDetailCacheEntry.From(detail, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await _redis.SetObjectAsync(Key(ticketId), entry, expiry: TimeSpan.FromHours(1), ct);
            _local.Set(ticketId, entry, LocalTtl);
            _log.LogInformation("L3 POPULATED: ticketId={Id} stockAvail={S}", ticketId, entry.StockAvailable);
            return entry;
        }
        finally
        {
            await distLock.ReleaseAsync(ct);
        }
    }

    public async Task SetAsync(FlashSale.Application.Services.TicketDetailCacheEntry entry, CancellationToken ct = default)
    {
        await _redis.SetObjectAsync(Key(entry.Id), entry, expiry: TimeSpan.FromHours(1), ct);
        _local.Set(entry.Id, entry, LocalTtl);
    }

    public async Task InvalidateAsync(long ticketId, CancellationToken ct = default)
    {
        await _redis.DeleteAsync(Key(ticketId), ct);
        _local.Remove(ticketId);
    }

    public async Task<bool> OrderByUserAsync(long ticketId, CancellationToken ct = default)
    {
        _local.Remove(ticketId);
        await _redis.DeleteAsync(Key(ticketId), ct);
        return true;
    }
}
