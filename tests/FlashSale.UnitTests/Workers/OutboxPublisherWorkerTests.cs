using System.Text.Json;
using FlashSale.Contracts.Messages;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using FlashSale.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FlashSale.UnitTests.Workers;

internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) { CurrentValue = value; }
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public class OutboxPublisherWorkerTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new(MockBehavior.Strict);
    private readonly Mock<IServiceScope> _scope = new(MockBehavior.Strict);
    private readonly Mock<IOutboxEventRepository> _outbox = new(MockBehavior.Strict);
    private readonly Mock<IKafkaOrderProducer> _producer = new(MockBehavior.Strict);
    private readonly Mock<IServiceProvider> _sp = new(MockBehavior.Strict);

    private readonly OutboxPublisherWorker _sut;

    public OutboxPublisherWorkerTests()
    {
        _scopeFactory.Setup(s => s.CreateScope()).Returns(_scope.Object);
        _scope.Setup(s => s.ServiceProvider).Returns(_sp.Object);
        _scope.Setup(s => s.Dispose());
        _sp.Setup(s => s.GetService(typeof(IOutboxEventRepository))).Returns(_outbox.Object);
        _sp.Setup(s => s.GetService(typeof(IKafkaOrderProducer))).Returns(_producer.Object);

        _sut = new OutboxPublisherWorker(
            _scopeFactory.Object,
            new StaticOptionsMonitor<KafkaOptions>(new KafkaOptions()),
            NullLogger<OutboxPublisherWorker>.Instance);
    }

    private static OutboxEvent PendingRow(long id, string token)
        => new()
        {
            Id = id,
            AggregateId = token,
            EventType = "ORDER_PLACED",
            Payload = JsonSerializer.Serialize(new PlaceOrderMqMessage(token, 4, 2, 5, 10_000, 1718246100123)),
            Status = 0,
            CreatedAt = DateTime.UtcNow,
        };

    private static readonly JsonSerializerOptions JsonOpts = new();

    [Fact]
    public async Task EmptyBatch_skips_publish_loop()
    {
        _outbox.Setup(s => s.FindPendingBatchAsync(OutboxPublisherWorker.DefaultBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutboxEvent>());

        await _sut.ExecuteOnceAsync(CancellationToken.None);

        _outbox.VerifyAll();
        _producer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HappyPath_publishes_then_marks_each_row_published()
    {
        var r1 = PendingRow(1, "MQ-tok-1");
        var r2 = PendingRow(2, "MQ-tok-2");

        _outbox.Setup(s => s.FindPendingBatchAsync(OutboxPublisherWorker.DefaultBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { r1, r2 });

        _producer.Setup(s => s.SendAndAwaitAckAsync(It.IsAny<string>(), "MQ-tok-1", It.IsAny<PlaceOrderMqMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _producer.Setup(s => s.SendAndAwaitAckAsync(It.IsAny<string>(), "MQ-tok-2", It.IsAny<PlaceOrderMqMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _outbox.Setup(s => s.MarkPublishedAsync(1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _outbox.Setup(s => s.MarkPublishedAsync(2, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.ExecuteOnceAsync(CancellationToken.None);

        _outbox.Verify(s => s.MarkPublishedAsync(1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(s => s.MarkPublishedAsync(2, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _producer.VerifyAll();
    }

    [Fact]
    public async Task KafkaFailure_does_not_mark_published_so_next_cycle_retries()
    {
        var r1 = PendingRow(1, "MQ-tok-1");
        var r2 = PendingRow(2, "MQ-tok-2");

        _outbox.Setup(s => s.FindPendingBatchAsync(OutboxPublisherWorker.DefaultBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { r1, r2 });

        _producer.Setup(s => s.SendAndAwaitAckAsync(It.IsAny<string>(), "MQ-tok-1", It.IsAny<PlaceOrderMqMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("kafka down"));
        _producer.Setup(s => s.SendAndAwaitAckAsync(It.IsAny<string>(), "MQ-tok-2", It.IsAny<PlaceOrderMqMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _outbox.Setup(s => s.MarkPublishedAsync(2, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.ExecuteOnceAsync(CancellationToken.None);

        _outbox.Verify(s => s.MarkPublishedAsync(1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _outbox.Verify(s => s.MarkPublishedAsync(2, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MalformedJson_skips_row_and_continues_with_next()
    {
        var good = PendingRow(2, "MQ-good");
        var bad = new OutboxEvent
        {
            Id = 1,
            AggregateId = "MQ-bad",
            EventType = "ORDER_PLACED",
            Payload = "{not-json",
            Status = 0,
            CreatedAt = DateTime.UtcNow,
        };

        _outbox.Setup(s => s.FindPendingBatchAsync(OutboxPublisherWorker.DefaultBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { bad, good });

        _producer.Setup(s => s.SendAndAwaitAckAsync(It.IsAny<string>(), "MQ-good", It.IsAny<PlaceOrderMqMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _outbox.Setup(s => s.MarkPublishedAsync(2, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.ExecuteOnceAsync(CancellationToken.None);

        _outbox.Verify(s => s.MarkPublishedAsync(1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _outbox.Verify(s => s.MarkPublishedAsync(2, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _producer.Verify(s => s.SendAndAwaitAckAsync(It.IsAny<string>(), "MQ-bad", It.IsAny<PlaceOrderMqMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NullPayload_skips_row()
    {
        var bad = new OutboxEvent
        {
            Id = 1,
            AggregateId = "MQ-bad",
            EventType = "ORDER_PLACED",
            Payload = "null",
            Status = 0,
            CreatedAt = DateTime.UtcNow,
        };

        _outbox.Setup(s => s.FindPendingBatchAsync(OutboxPublisherWorker.DefaultBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { bad });

        await _sut.ExecuteOnceAsync(CancellationToken.None);

        _outbox.Verify(s => s.MarkPublishedAsync(It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _producer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cancellation_during_cycle_aborts_quickly()
    {
        _outbox.Setup(s => s.FindPendingBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => _sut.ExecuteOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MarkPublishedFailure_is_caught_and_cycle_continues()
    {
        var r1 = PendingRow(1, "MQ-tok-1");
        var r2 = PendingRow(2, "MQ-tok-2");

        _outbox.Setup(s => s.FindPendingBatchAsync(OutboxPublisherWorker.DefaultBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { r1, r2 });

        _producer.Setup(s => s.SendAndAwaitAckAsync(It.IsAny<string>(), "MQ-tok-1", It.IsAny<PlaceOrderMqMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _producer.Setup(s => s.SendAndAwaitAckAsync(It.IsAny<string>(), "MQ-tok-2", It.IsAny<PlaceOrderMqMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _outbox.Setup(s => s.MarkPublishedAsync(1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db transient"));
        _outbox.Setup(s => s.MarkPublishedAsync(2, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // The catch is INSIDE the per-row loop, not around the whole cycle,
        // so a MarkPublished failure on r1 must NOT prevent r2 from being processed.
        await _sut.ExecuteOnceAsync(CancellationToken.None);

        _outbox.Verify(s => s.MarkPublishedAsync(1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(s => s.MarkPublishedAsync(2, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

internal static class JsonSerializer
{
    // Local facade so tests can use a static JsonSerializer.Serialize without
    // pulling System.Text.Json into the global usings in a way that confuses the API project.
    public static string Serialize<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);
    public static T? Deserialize<T>(string json) => System.Text.Json.JsonSerializer.Deserialize<T>(json);
}