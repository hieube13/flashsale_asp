using System.Text.Json.Serialization;

namespace FlashSale.Contracts.Dto;

/// <summary>
/// Request body for POST /order/cas.
/// Mirrors Java CreateBookingRequest fields but uses camelCase JSON keys
/// to match the frontend's axios payload { ticketId, quantity }.
public sealed record PlaceOrderRequest(
    [property: JsonPropertyName("ticketId")] long TicketId,
    [property: JsonPropertyName("quantity")] int Quantity);
