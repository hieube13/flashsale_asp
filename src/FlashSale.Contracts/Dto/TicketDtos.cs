namespace FlashSale.Contracts.Dto;

/// <summary>
/// Output DTO for ticket endpoints.
/// Mirrors Java TicketDTO.
/// </summary>
public sealed record TicketDto(
    long Id,
    string Name,
    string? Description,
    DateTime StartTime,
    DateTime EndTime,
    int Status);

public sealed record TicketDetailDto(
    long Id,
    string Name,
    string? Description,
    int StockInitial,
    int StockAvailable,
    bool IsStockPrepared,
    decimal PriceOriginal,
    decimal PriceFlash,
    DateTime SaleStartTime,
    DateTime SaleEndTime,
    int Status,
    long ActivityId);

public sealed record TicketOrderDto(
    int Id,
    int UserId,
    int TicketId,
    int Quantity,
    int OrderStatus,
    string OrderNumber,
    decimal TotalAmount,
    string TerminalId,
    DateTime OrderDate,
    string? OrderNotes,
    DateTime UpdatedAt,
    DateTime CreatedAt);

public sealed record PagedOrdersDto(
    IReadOnlyList<TicketOrderDto> Items,
    long? NextCursor,
    bool HasMore);

public sealed record BookingDto(
    long Id,
    long TicketId,
    int Quantity,
    string BookingCode,
    int Status,
    DateTime CreatedAt);