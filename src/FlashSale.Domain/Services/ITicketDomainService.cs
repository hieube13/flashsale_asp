using FlashSale.Domain.Entities;

namespace FlashSale.Domain.Services;

/// <summary>
/// Domain service for Ticket + TicketDetail business invariants.
/// Mirrors Java com.xxxx.ddd.domain.service.TicketDomainService.
/// </summary>
public interface ITicketDomainService
{
    Task<Ticket> CreateAsync(Ticket ticket, TicketDetail detail, CancellationToken ct = default);
    Task<Ticket> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<Ticket>> GetAllActiveAsync(CancellationToken ct = default);
    Task<Ticket> UpdateAsync(long id, Ticket updated, CancellationToken ct = default);
    Task<Ticket> ActivateAsync(long id, CancellationToken ct = default);
    Task<Ticket> DeactivateAsync(long id, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

/// <summary>
/// Domain service for TicketDetail lookups.
/// Mirrors Java TicketDetailDomainService.
/// </summary>
public interface ITicketDetailDomainService
{
    Task<TicketDetail?> GetByIdAsync(long id, CancellationToken ct = default);
}
