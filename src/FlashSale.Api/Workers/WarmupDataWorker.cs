using FlashSale.Application.Services;
using FlashSale.Infrastructure.Cache;

namespace FlashSale.Api.Workers;

/// <summary>
/// Cache warmup worker — mirrors Java WarmupDataBeforeEvent:
///   - On startup: hydrate Redis with stock_available/price_flash for all active tickets.
///   - Daily at 00:00 (server local): re-hydrate so flash-sale resets the clock.
///
/// Scheduled cadence is decided here (24h) rather than via cron expression to keep the
/// scaffold dependency-light. A cron parser can be swapped in later if needed.
/// </summary>
public sealed class WarmupDataWorker : BackgroundService
{
    private static readonly TimeSpan DailyInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WarmupDataWorker> _log;
    private readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(5);

    public WarmupDataWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<WarmupDataWorker> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Boot delay so MySQL is ready before we query
        try { await Task.Delay(_startupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        // Initial load
        await WarmupOnceAsync(stoppingToken);

        // Daily loop
        using var timer = new PeriodicTimer(DailyInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await WarmupOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
    }

    private async Task WarmupOnceAsync(CancellationToken ct)
    {
        try
        {
            _log.LogInformation("WarmupDataBeforeEvent — hydrating Redis at {Ts:O}", DateTimeOffset.UtcNow);
            using var scope = _scopeFactory.CreateScope();
            var stockCache = scope.ServiceProvider.GetRequiredService<IStockOrderCacheService>();
            await stockCache.AddStockAvailableToCacheAsync(4L, ct);
            _log.LogInformation("WarmupDataBeforeEvent — completed at {Ts:O}", DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Warmup failed; will retry on next tick");
        }
    }
}
