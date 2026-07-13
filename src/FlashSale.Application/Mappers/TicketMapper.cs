using FlashSale.Contracts.Dto;
using FlashSale.Domain.Entities;

namespace FlashSale.Application.Mappers;

/// <summary>
/// Pure DTO ↔ Entity mapping for Ticket and TicketDetail.
/// Mirrors Java application.mapper.TicketMapper + TicketDetailMapper.
/// </summary>
public static class TicketMapper
{
    public static TicketDto ToDto(Ticket t, TicketDetail? firstDetail = null)
    {
        if (firstDetail is null)
            return new TicketDto(t.Id, t.Name, t.Description, t.StartTime, t.EndTime, t.Status);

        return new TicketDto(
            Id: t.Id,
            Name: t.Name,
            Description: t.Description,
            StartTime: t.StartTime,
            EndTime: t.EndTime,
            Status: t.Status,
            PriceOriginal: firstDetail.PriceOriginal,
            PriceFlash: firstDetail.PriceFlash,
            StockInitial: firstDetail.StockInitial,
            StockAvailable: firstDetail.StockAvailable);
    }

    public static TicketDetailDto ToDto(TicketDetail d) =>
        new(d.Id, d.Name, d.Description, d.StockInitial, d.StockAvailable, d.IsStockPrepared,
            d.PriceOriginal, d.PriceFlash, d.SaleStartTime, d.SaleEndTime, d.Status, d.ActivityId);

    /// <summary>
    /// Build a Ticket entity from the create request payload.
    /// Mirrors Java TicketMapper.toEntity(CreateTicketCommand).
    /// </summary>
    public static Ticket ToEntity(CreateTicketRequest r) =>
        new()
        {
            Name = r.Name,
            Description = r.Description ?? string.Empty,
            StartTime = r.StartTime,
            EndTime = r.EndTime,
            Status = 1, // active on create
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

    /// <summary>
    /// Build a TicketDetail entity from the create request payload.
    /// Mirrors Java TicketDetailMapper.toEntity(CreateTicketDetailCommand).
    /// </summary>
    public static TicketDetail ToEntity(CreateTicketDetailRequest r) =>
        new()
        {
            Name = r.Name,
            // Fallback to requested stockAvailable if provided, else initial
            StockInitial = r.StockInitial,
            StockAvailable = r.StockAvailable > 0 ? r.StockAvailable : r.StockInitial,
            PriceOriginal = r.PriceOriginal,
            PriceFlash = 0m,
            IsStockPrepared = false,
            Status = 1,
            SaleStartTime = DateTime.UtcNow,
            SaleEndTime = DateTime.UtcNow.AddMonths(6),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
}
