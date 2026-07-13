using FlashSale.Contracts.Dto;
using FlashSale.Contracts.Messages;
using FlashSale.Domain.Entities;

namespace FlashSale.Application.Services;

/// <summary>
/// OrderMQ service — async place order via Kafka.
/// Mirrors Java OrderMQAppService + KafkaOrderConsumer.
/// </summary>
public interface IOrderMqAppService
{
    Task<OrderQueue> PlaceOrderMqAsync(long ticketId, int quantity, CancellationToken ct = default);
    Task<OrderQueue?> GetOrderStatusAsync(string token, CancellationToken ct = default);
}

/// <summary>
/// Kafka consumer handler — splits from interface to allow running in BackgroundService.
/// </summary>
public interface IOrderMqConsumerHandler
{
    Task ProcessAsync(PlaceOrderMqMessage message, CancellationToken ct);
}