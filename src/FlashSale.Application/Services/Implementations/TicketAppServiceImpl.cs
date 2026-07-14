using FlashSale.Application.Mappers;
using FlashSale.Application.Services;
using FlashSale.Contracts.Dto;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using FlashSale.Domain.Services;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Services.Implementations;

/// <summary>
/// Application service for the catalog slice — mirrors Java TicketAppServiceImpl.
/// Responsibilities: orchestrate Ticket ↔ TicketDetail, write-through cache,
/// enrich the <see cref="TicketDto"/> with the first detail's price/stock so the
/// frontend can render a single row without an extra roundtrip.
/// </summary>
public sealed class TicketAppServiceImpl : ITicketAppService
{
    private readonly ITicketDomainService _domain;
    private readonly ITicketDetailRepository _details;
    private readonly ITicketCacheService _ticketCache;
    private readonly ITicketDetailCacheService _detailCache;
    private readonly ILogger<TicketAppServiceImpl> _log;

    public TicketAppServiceImpl(
        ITicketDomainService domain,
        ITicketDetailRepository details,
        ITicketCacheService ticketCache,
        ITicketDetailCacheService detailCache,
        ILogger<TicketAppServiceImpl> log)
    {
        _domain = domain;
        _details = details;
        _ticketCache = ticketCache;
        _detailCache = detailCache;
        _log = log;
    }

    public async Task<IReadOnlyList<TicketDto>> GetAllActiveAsync(CancellationToken ct = default)
    {
        var tickets = await _domain.GetAllActiveAsync(ct);
        var result = new List<TicketDto>(tickets.Count);
        foreach (var t in tickets)
        {
            var detailList = await _details.FindByActivityIdAsync(t.Id, ct);
            result.Add(TicketMapper.ToDto(t, detailList.FirstOrDefault()));
        }
        return result;
    }

    public async Task<TicketDto> GetByIdAsync(long ticketId, CancellationToken ct = default)
    {
        var ticket = await _domain.GetByIdAsync(ticketId, ct);
        var details = await _details.FindByActivityIdAsync(ticket.Id, ct);
        return TicketMapper.ToDto(ticket, details.FirstOrDefault());
    }

    public async Task<TicketDto> CreateAsync(
        CreateTicketRequest ticket,
        CreateTicketDetailRequest detail,
        CancellationToken ct = default)
    {
        var ticketEntity = TicketMapper.ToEntity(ticket);
        var detailEntity = TicketMapper.ToEntity(detail);

        var created = await _domain.CreateAsync(ticketEntity, detailEntity, ct);
        var persistedDetail = (await _details.FindByActivityIdAsync(created.Id, ct)).First();
        // After domain persists both rows, populate both caches (write-through, mirrors Java)
        await _ticketCache.SetAsync(created.Id,
            new TicketCacheSnapshot(created.Id, created.Name, created.Description,
                created.StartTime, created.EndTime, created.Status), ct);
        await _detailCache.SetAsync(
            TicketDetailCacheEntry.From(persistedDetail, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ct);
        _log.LogInformation("Created & cached ticket {Id} with detail", created.Id);
        return TicketMapper.ToDto(created, persistedDetail);
    }

    public async Task<TicketDto> UpdateAsync(long ticketId, UpdateTicketRequest req, CancellationToken ct = default)
    {
        var stub = new Ticket
        {
            Name = req.Name ?? string.Empty,
            Description = req.Description,
            StartTime = req.StartTime ?? DateTime.UtcNow,
            EndTime = req.EndTime ?? DateTime.UtcNow.AddMonths(1),
        };
        var updated = await _domain.UpdateAsync(ticketId, stub, ct);
        await _ticketCache.SetAsync(updated.Id,
            new TicketCacheSnapshot(updated.Id, updated.Name, updated.Description,
                updated.StartTime, updated.EndTime, updated.Status), ct);
        var details = await _details.FindByActivityIdAsync(updated.Id, ct);
        return TicketMapper.ToDto(updated, details.FirstOrDefault());
    }

    public async Task<TicketDto> ActivateAsync(long ticketId, CancellationToken ct = default)
    {
        var t = await _domain.ActivateAsync(ticketId, ct);
        await _ticketCache.SetAsync(t.Id,
            new TicketCacheSnapshot(t.Id, t.Name, t.Description, t.StartTime, t.EndTime, t.Status), ct);
        var details = await _details.FindByActivityIdAsync(t.Id, ct);
        return TicketMapper.ToDto(t, details.FirstOrDefault());
    }

    public async Task<TicketDto> DeactivateAsync(long ticketId, CancellationToken ct = default)
    {
        var t = await _domain.DeactivateAsync(ticketId, ct);
        await _ticketCache.SetAsync(t.Id,
            new TicketCacheSnapshot(t.Id, t.Name, t.Description, t.StartTime, t.EndTime, t.Status), ct);
        var details = await _details.FindByActivityIdAsync(t.Id, ct);
        return TicketMapper.ToDto(t, details.FirstOrDefault());
    }

    public async Task DeleteAsync(long ticketId, CancellationToken ct = default)
    {
        await _domain.DeleteAsync(ticketId, ct);
        await _ticketCache.EvictAsync(ticketId, ct);
    }
}

public sealed class TicketDetailAppServiceImpl : ITicketDetailAppService
{
    private readonly ITicketDetailCacheService _cache;
    private readonly ILogger<TicketDetailAppServiceImpl> _log;

    public TicketDetailAppServiceImpl(
        ITicketDetailCacheService cache,
        ILogger<TicketDetailAppServiceImpl> log)
    {
        _cache = cache;
        _log = log;
    }

    public async Task<TicketDetailDto> GetByIdAsync(long detailId, long? version, CancellationToken ct = default)
    {
        var entry = await _cache.GetAsync(detailId, version, ct);
        if (entry is null)
            throw new InvalidOperationException($"TicketDetail {detailId} not found");

        return new TicketDetailDto(
            entry.Id,
            entry.Name,
            Description: null,
            entry.StockInitial,
            entry.StockAvailable,
            IsStockPrepared: false,
            entry.PriceOriginal,
            entry.PriceFlash,
            entry.SaleStartTime,
            entry.SaleEndTime,
            entry.Status,
            entry.ActivityId,
            entry.Version);
    }

    public async Task<bool> OrderByUserAsync(long detailId, CancellationToken ct = default)
    {
        var result = await _cache.OrderByUserAsync(detailId, ct);
        _log.LogInformation("OrderByUser detailId={Id} → {Result}", detailId, result);
        return result;
    }
}
