namespace FlashSale.Contracts.Dto;

/// <summary>
/// Request body for POST /order/cas.
/// Mirrors Java CreateBookingRequest.
/// </summary>
public sealed record CreateBookingRequest(
    long TicketId,
    int Quantity);