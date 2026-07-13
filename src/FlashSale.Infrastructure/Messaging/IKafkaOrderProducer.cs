namespace FlashSale.Infrastructure.Messaging;

/// <summary>
/// Kafka producer abstraction.
/// </summary>
public interface IKafkaOrderProducer
{
    /// <summary>Send with broker ACK — returns only after broker confirms persistence.</summary>
    Task SendAndAwaitAckAsync<T>(string topic, string key, T message, CancellationToken ct = default);
}