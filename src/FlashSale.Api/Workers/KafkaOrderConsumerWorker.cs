using System.Text.Json;
using Confluent.Kafka;
using FlashSale.Application.Services;
using FlashSale.Contracts.Messages;
using FlashSale.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace FlashSale.Api.Workers;

/// <summary>
/// Kafka consumer worker — pulls <see cref="PlaceOrderMqMessage"/> from
/// <c>order-place-topic</c> and dispatches to
/// <see cref="IOrderMqConsumerHandler.ProcessAsync"/> in a fresh DI scope per
/// message (so EF Core DbContext + scoped repos stay correct under concurrency).
/// <para>
/// Concrete implementation of TASK-016. Mirrors Java
/// <c>KafkaOrderConsumer.@KafkaListener(topics=…, groupId=…, concurrency="10")</c>
/// — see Java source lines 32-37. Confluent.Kafka is already in
/// <c>FlashSale.Infrastructure</c> from TASK-007.
/// </para>
/// <list type="bullet">
///   <item><b>Manual offset commit</b> (per-message ack) — matches Java's
///         <c>ContainerAcknowledgementMode.MANUAL</c> default. Offset is
///         committed AFTER <c>ProcessAsync</c> returns without throwing.
///         If the worker dies mid-batch, Kafka redelivers to the same
///         partition on rebalance — idempotency_key gate absorbs the
///         duplicate (see <c>OrderMqConsumerHandlerImpl</c>).</item>
///   <item><b>Retry policy</b>: 3 attempts with exponential backoff
///         (200 ms → 400 ms → 800 ms) before the offset is committed
///         (poison-pill escape hatch — same as Java's no-retry + DLQ
///         default; we avoid blocking the partition).</item>
///   <item><b>Concurrency</b>: single BackgroundService runs the
///         <c>IConsumer.Consume</c> loop on one thread; per-message
///         processing is sequential to keep stock decrement ordering
///         predictable. Scale-out happens by running multiple API
///         processes in the same consumer group.</item>
/// </list>
/// </summary>
public sealed class KafkaOrderConsumerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<KafkaOptions> _options;
    private readonly ILogger<KafkaOrderConsumerWorker> _log;
    private readonly IConsumer<string, string>? _consumer;

    public KafkaOrderConsumerWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<KafkaOptions> options,
        ILogger<KafkaOrderConsumerWorker> log)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _log = log;

        var cfg = options.CurrentValue;
        if (string.IsNullOrEmpty(cfg.BootstrapServers))
        {
            _consumer = null;
            return;
        }

        var conf = new ConsumerConfig
        {
            BootstrapServers = cfg.BootstrapServers,
            GroupId = cfg.ConsumerGroupId,
            EnableAutoCommit = false,    // manual ack — mirror Java MANUAL_IMMEDIATE
            AutoOffsetReset = AutoOffsetReset.Earliest,
            SessionTimeoutMs = 10_000,
            MaxPollIntervalMs = 60_000,
            // EnablePartitionEof = false;
        };
        _consumer = new ConsumerBuilder<string, string>(conf)
            .SetErrorHandler((_, e) => _log.LogError("Kafka consumer error: {Reason}", e.Reason))
            .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_consumer is null)
        {
            _log.LogWarning("KafkaOrderConsumerWorker started without bootstrap servers — sleeping");
            try { await Task.Delay(Timeout.Infinite, stoppingToken); }
            catch (OperationCanceledException) { }
            return;
        }

        var topic = _options.CurrentValue.OrderPlaceTopic;
        _consumer.Subscribe(topic);
        _log.LogInformation("KafkaOrderConsumer subscribed topic={Topic} group={Group}",
            topic, _options.CurrentValue.ConsumerGroupId);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = _consumer.Consume(TimeSpan.FromSeconds(1));
                if (result is null) continue; // poll timeout — loop
            }
            catch (OperationCanceledException) { break; }
            catch (ConsumeException ex)
            {
                _log.LogError(ex, "Consume failed, sleeping 1s");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            // Dispatch one message per loop iteration; retries with backoff.
            var ok = await ProcessWithRetryAsync(result.Message.Value, stoppingToken);
            if (ok)
            {
                try { _consumer.Commit(result); }
                catch (KafkaException ex) { _log.LogWarning(ex, "Commit failed for offset {Offset}", result.Offset); }
            }
            else
            {
                // Poison-pill: log + commit so the partition does not stall.
                _log.LogError("Abandoning unprocessable message offset={Offset} key={Key}", result.Offset, result.Message.Key);
                try { _consumer.Commit(result); }
                catch (KafkaException) { }
            }
        }

        try { _consumer.Close(); } catch { /* swallowed on shutdown */ }
    }

    /// <summary>
    /// 3 retries with 200 ms → 400 ms → 800 ms backoff. Returns true on success.
    /// </summary>
    private async Task<bool> ProcessWithRetryAsync(string json, CancellationToken ct)
    {
        PlaceOrderMqMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<PlaceOrderMqMessage>(json);
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "malformed Kafka payload: {Json}", json);
            return false;
        }
        if (msg is null)
        {
            _log.LogError("null payload: {Json}", json);
            return false;
        }

        var delays = new[] { TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(800) };
        for (var attempt = 0; attempt <= delays.Length; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IOrderMqConsumerHandler>();
                // Java uses a 30 s timeout per message; we mirror via a 30 s linked CTS.
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                await handler.ProcessAsync(msg, cts.Token);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == delays.Length)
                {
                    _log.LogError(ex, "processOrder exhausted retries token={Token}", msg.Token);
                    return false;
                }
                _log.LogWarning(ex, "processOrder attempt {Attempt} failed token={Token} — retrying in {Delay}",
                    attempt + 1, msg.Token, delays[attempt]);
                try { await Task.Delay(delays[attempt], ct); }
                catch (OperationCanceledException) { return false; }
            }
        }
        return false;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { _consumer?.Close(); } catch { }
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}