using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;

namespace FlashSale.Domain.Services;

/// <summary>
/// Concrete ticket domain service — wraps repository operations with the
/// Java-style invariants (active-only queries, soft-delete semantics,
/// cascading TicketDetail on create).
/// </summary>
public sealed class TicketDomainService : ITicketDomainService
{
    private readonly ITicketRepository _tickets;
    private readonly ITicketDetailRepository _details;

    public TicketDomainService(ITicketRepository tickets, ITicketDetailRepository details)
    {
        _tickets = tickets;
        _details = details;
    }

    public async Task<Ticket> CreateAsync(Ticket ticket, TicketDetail detail, CancellationToken ct = default)
    {
        // Defaults
        if (ticket.Status == 0) ticket.Status = 1; // active on create (mirrors Java behaviour)
        ticket.CreatedAt = DateTime.UtcNow;
        ticket.UpdatedAt = ticket.CreatedAt;

        var created = await _tickets.AddAsync(ticket, ct);

        // Linked detail — ActivityId points back to the Ticket
        detail.ActivityId = created.Id;
        if (detail.CreatedAt == default) detail.CreatedAt = DateTime.UtcNow;
        detail.UpdatedAt = detail.CreatedAt;

        await _details.AddAsync(detail, ct);
        return created;
    }

    public Task<Ticket> GetByIdAsync(long id, CancellationToken ct = default)
        => _tickets.GetByIdAsync(id, ct)
            .ContinueWith(t => t.Result ?? throw new InvalidOperationException($"Ticket {id} not found"), ct);

    public Task<IReadOnlyList<Ticket>> GetAllActiveAsync(CancellationToken ct = default)
        => _tickets.GetActiveAsync(ct);

    public async Task<Ticket> UpdateAsync(long id, Ticket updated, CancellationToken ct = default)
    {
        var existing = await _tickets.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"Ticket {id} not found");
        existing.Name = updated.Name;
        existing.Description = updated.Description;
        existing.StartTime = updated.StartTime;
        existing.EndTime = updated.EndTime;
        existing.UpdatedAt = DateTime.UtcNow;
        await _tickets.UpdateAsync(existing, ct);
        return existing;
    }

    public async Task<Ticket> ActivateAsync(long id, CancellationToken ct = default)
    {
        var t = await _tickets.GetByIdAsync(id, ct) ?? throw new InvalidOperationException($"Ticket {id} not found");
        t.Status = 1;
        t.UpdatedAt = DateTime.UtcNow;
        await _tickets.UpdateAsync(t, ct);
        return t;
    }

    public async Task<Ticket> DeactivateAsync(long id, CancellationToken ct = default)
    {
        var t = await _tickets.GetByIdAsync(id, ct) ?? throw new InvalidOperationException($"Ticket {id} not found");
        t.Status = 0;
        t.UpdatedAt = DateTime.UtcNow;
        await _tickets.UpdateAsync(t, ct);
        return t;
    }

    public Task DeleteAsync(long id, CancellationToken ct = default)
        => _tickets.SoftDeleteAsync(id, ct);
}

public sealed class TicketDetailDomainService : ITicketDetailDomainService
{
    private readonly ITicketDetailRepository _repo;
    public TicketDetailDomainService(ITicketDetailRepository repo) => _repo = repo;

    public Task<TicketDetail?> GetByIdAsync(long id, CancellationToken ct = default)
        => _repo.GetByIdAsync(id, ct);
}
