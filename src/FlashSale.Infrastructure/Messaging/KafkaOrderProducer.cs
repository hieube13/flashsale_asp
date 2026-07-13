using Confluent.Kafka;
using FlashSale.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace FlashSale.Infrastructure.Messaging;

/// <summary>
/// Confluent.Kafka producer stub.
/// Real broker ACK semantics added in TASK-007/017.
/// </summary>
public sealed class KafkaOrderProducer : IKafkaOrderProducer, IDisposable
{
    private readonly IProducer<string, string>? _producer;

    public KafkaOrderProducer(IOptions<KafkaOptions> opts)
    {
        var cfg = opts.Value;
        if (string.IsNullOrEmpty(cfg.BootstrapServers)) return;
        var conf = new ProducerConfig
        {
            BootstrapServers = cfg.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 5000
        };
        _producer = new ProducerBuilder<string, string>(conf).Build();
    }

    public async Task SendAndAwaitAckAsync<T>(string topic, string key, T message, CancellationToken ct = default)
    {
        if (_producer is null)
            throw new InvalidOperationException("Kafka producer not initialised — set Kafka:BootstrapServers");

        var json = System.Text.Json.JsonSerializer.Serialize(message);
        await _producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = json }, ct);
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(5));
        _producer?.Dispose();
    }
}

public sealed class KafkaOptions
{
    public string? BootstrapServers { get; set; }
    public string OrderPlaceTopic { get; set; } = "order-place-topic";
    public string ConsumerGroupId { get; set; } = "order-consumer-group";
}