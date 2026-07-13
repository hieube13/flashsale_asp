namespace FlashSale.Contracts.Messages;

/// <summary>
/// Message published to Kafka topic order-place-topic.
/// Producer writes it to outbox_event.payload; consumer reads from topic.
/// </summary>
public sealed record PlaceOrderMqMessage(
    string Token,
    long TicketId,
    int Quantity,
    int UserId,
    long UnitPrice,
    long CreatedAt);