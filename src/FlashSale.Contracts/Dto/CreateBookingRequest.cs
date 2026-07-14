namespace FlashSale.Contracts.Dto;

/// <summary>
/// Request body for POST /api/bookings.
/// Mirrors Java CreateBookingRequest (xxxx-controller/.../dto/CreateBookingRequest.java).
/// </summary>
public sealed record CreateBookingRequest(
    long TicketId,
    int Quantity);