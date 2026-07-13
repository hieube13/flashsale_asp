namespace FlashSale.Contracts.Dto;

/// <summary>
/// Request body for POST /order/mq.
/// Mirrors Java PlaceOrderMQRequest.
/// </summary>
public sealed record PlaceOrderMqRequest(
    long TicketId,
    int Quantity);