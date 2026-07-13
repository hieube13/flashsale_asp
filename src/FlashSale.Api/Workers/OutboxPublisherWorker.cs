namespace FlashSale.Api.Workers;

/// <summary>
/// Outbox publisher worker — concrete impl added in TASK-017.
/// Reads outbox_event PENDING → publishes Kafka → marks PUBLISHED.
/// </summary>
public sealed class OutboxPublisherWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Concrete polling loop + SELECT ... FOR UPDATE SKIP LOCKED added in TASK-017.
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
    }
}