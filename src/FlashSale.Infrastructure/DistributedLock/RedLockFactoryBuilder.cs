using Microsoft.Extensions.Logging;
using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace FlashSale.Infrastructure.DistributedLock;

/// <summary>
/// Builds <see cref="RedLockFactory"/> instances from an existing
/// <see cref="IConnectionMultiplexer"/>. The factory creation uses
/// reflection so the Infrastructure project does not need to expose a
/// hard reference to <c>RedLockNet.SERedis.RedLockEndPoint</c> as part
/// of the public API surface (the type lives in RedLockNet.Abstractions
/// but only the SERedis factory has the matching Create overload that
/// accepts it).
/// </summary>
public static class RedLockFactoryBuilder
{
    public static RedLockFactory Build(IConnectionMultiplexer mux, ILogger logger)
    {
        var endpoints = mux.GetEndPoints()
            .Select(ep => new RedLockEndPoint { EndPoint = ep })
            .ToList();
        logger.LogInformation(
            "Building RedLockFactory with {Count} endpoint(s): {Endpoints}",
            endpoints.Count,
            string.Join(", ", endpoints.Select(e => e.EndPoint?.ToString() ?? "(null)")));
        return RedLockNet.SERedis.RedLockFactory.Create(endpoints);
    }
}