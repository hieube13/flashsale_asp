using FlashSale.Contracts.Dto;

namespace FlashSale.Application.Services;

/// <summary>
/// Catalog service — mirrors Java TicketAppService + TicketDetailAppService.
/// </summary>
public interface ITicketAppService
{
    Task<IReadOnlyList<TicketDto>> GetAllActiveAsync(CancellationToken ct = default);
    Task<TicketDto> GetByIdAsync(long ticketId, CancellationToken ct = default);
    Task<TicketDto> CreateAsync(CreateTicketRequest ticket, CreateTicketDetailRequest detail, CancellationToken ct = default);
    Task<TicketDto> UpdateAsync(long ticketId, UpdateTicketRequest req, CancellationToken ct = default);
    Task<TicketDto> ActivateAsync(long ticketId, CancellationToken ct = default);
    Task<TicketDto> DeactivateAsync(long ticketId, CancellationToken ct = default);
    Task DeleteAsync(long ticketId, CancellationToken ct = default);
}

public interface ITicketDetailAppService
{
    Task<TicketDetailDto> GetByIdAsync(long detailId, long? version, CancellationToken ct = default);
    Task<bool> OrderByUserAsync(long detailId, CancellationToken ct = default);
}