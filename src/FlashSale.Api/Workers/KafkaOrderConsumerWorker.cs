using FlashSale.Application.Services;
using FlashSale.Contracts.Messages;

namespace FlashSale.Api.Workers;

/// <summary>
/// Kafka consumer worker — concrete impl added in TASK-016.
/// This stub drains topic without committing offsets — useful for confirming scaffold wiring.
/// </summary>
public sealed class KafkaOrderConsumerWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Concrete consumer (Confluent.Kafka IConsumer) added in TASK-016.
        // Until then, sleep quietly so the host can boot.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
    }
}