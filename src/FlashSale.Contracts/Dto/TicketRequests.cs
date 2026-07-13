namespace FlashSale.Contracts.Dto;

/// <summary>
/// Request body for POST /ticket/create.
/// Mirrors Java CreateTicketFullRequest.
/// </summary>
public sealed record CreateTicketFullRequest(
    CreateTicketRequest Ticket,
    CreateTicketDetailRequest Detail);

public sealed record CreateTicketRequest(
    string Name,
    string? Description,
    DateTime StartTime,
    DateTime EndTime);

public sealed record CreateTicketDetailRequest(
    string Name,
    int StockInitial,
    int StockAvailable,
    decimal PriceOriginal);

public sealed record UpdateTicketRequest(
    string? Name,
    string? Description,
    DateTime? StartTime,
    DateTime? EndTime);