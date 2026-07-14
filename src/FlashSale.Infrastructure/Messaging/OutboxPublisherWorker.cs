using System.Text.Json;
using FlashSale.Contracts.Messages;
using FlashSale.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlashSale.Infrastructure.Messaging;

/// <summary>
/// Outbox publisher worker — concrete implementation of TASK-017.
/// Mirrors Java <c>OutboxPublisherJob.publishRowByRow()</c> (lines 55-80):
/// each tick reads a PENDING batch from <c>outbox_event</c>, sends each
/// payload to Kafka (awaiting broker ACK), and flips <c>status</c> to
/// PUBLISHED only after the broker confirms. On any per-row failure the
/// row is left PENDING — the next cycle retries automatically.
/// <para>
/// We deliberately choose the row-by-row mode (Java default) over the
/// batch mode because the failure window is narrower (only the in-flight
/// row is at risk if the worker dies) and debugging partial failures is
/// straightforward. A future task can add <c>publishBatch()</c> as an
/// opt-in via config flag if throughput demands it.
/// </para>
/// </summary>
public sealed class OutboxPublisherWorker : BackgroundService
{
    public const int DefaultBatchSize = 500;
    public const int DefaultPollDelayMs = 1_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<KafkaOptions> _options;
    private readonly ILogger<OutboxPublisherWorker> _log;

    public OutboxPublisherWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<KafkaOptions> options,
        ILogger<OutboxPublisherWorker> log)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small startup jitter so multiple API instances don't stampede the
        // outbox table on cold start. Cheap and not configurable.
        try { await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Defensive: anything escaping ExecuteOnceAsync must not kill the worker.
                _log.LogError(ex, "OutboxPublisher unhandled error — sleeping before next cycle");
            }

            try { await Task.Delay(TimeSpan.FromMilliseconds(DefaultPollDelayMs), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Single poll cycle — public so unit tests can drive it deterministically
    /// without waiting for the timer.
    /// </summary>
    public async Task ExecuteOnceAsync(CancellationToken ct)
    {
        // Fresh DI scope per cycle so the EF DbContext is short-lived and the
        // Mongo / Redis clients get the same lifetime semantics as a request.
        using var scope = _scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();
        var producer = scope.ServiceProvider.GetRequiredService<IKafkaOrderProducer>();

        var topic = _options.CurrentValue.OrderPlaceTopic;
        var pending = await outbox.FindPendingBatchAsync(DefaultBatchSize, ct);
        if (pending.Count == 0) return;

        _log.LogDebug("OutboxPublisher cycle: {Count} PENDING events", pending.Count);

        foreach (var ev in pending)
        {
            ct.ThrowIfCancellationRequested();

            PlaceOrderMqMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<PlaceOrderMqMessage>(ev.Payload);
            }
            catch (JsonException ex)
            {
                // Malformed payload — log + skip. Marking PUBLISHED would lose
                // the row forever; leaving PENDING makes the cycle noisy but
                // recoverable. A future ops task can add a dead-letter table.
                _log.LogError(ex, "OutboxPublisher parse failed eventId={Id} aggregateId={Agg}",
                    ev.Id, ev.AggregateId);
                continue;
            }
            if (message is null)
            {
                _log.LogError("OutboxPublisher null payload eventId={Id} aggregateId={Agg}",
                    ev.Id, ev.AggregateId);
                continue;
            }

            try
            {
                // Send + await broker ACK (matches Java line 67 — kafkaOrderProducer.sendAndAwaitAck).
                await producer.SendAndAwaitAckAsync(topic, message.Token, message, ct);
                await outbox.MarkPublishedAsync(ev.Id, DateTime.UtcNow, ct);

                _log.LogDebug("OutboxPublisher published eventId={Id} token={Token}",
                    ev.Id, ev.AggregateId);
            }
            catch (Exception ex)
            {
                // Kafka fail or ACK timeout → DO NOT mark PUBLISHED → next cycle retries.
                _log.LogError(ex, "OutboxPublisher kafka send failed eventId={Id} token={Token} — will retry next cycle",
                    ev.Id, ev.AggregateId);
            }
        }
    }
}