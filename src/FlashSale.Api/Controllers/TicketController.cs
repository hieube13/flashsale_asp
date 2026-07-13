using FlashSale.Application.Services;
using FlashSale.Contracts.Dto;
using Microsoft.AspNetCore.Mvc;

namespace FlashSale.Api.Controllers;

/// <summary>
/// Catalog endpoint — mirrors Java TicketController (7 endpoints).
/// Response envelope is <see cref="ResultMessage{T}"/> to keep parity with Java clients.
/// </summary>
[ApiController]
[Route("ticket")]
public sealed class TicketController : ControllerBase
{
    private readonly ITicketAppService _service;
    private readonly ILogger<TicketController> _log;

    public TicketController(ITicketAppService service, ILogger<TicketController> log)
    {
        _service = service;
        _log = log;
    }

    /// <summary>GET /ticket/active — all tickets where status=1</summary>
    [HttpGet("active")]
    public async Task<ResultMessage<IReadOnlyList<TicketDto>>> GetAllActiveAsync(CancellationToken ct)
    {
        try
        {
            var data = await _service.GetAllActiveAsync(ct);
            return ResultMessage<IReadOnlyList<TicketDto>>.Data(data);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error fetching active tickets");
            return ResultMessage<IReadOnlyList<TicketDto>>.Error(500, "Failed to fetch active tickets");
        }
    }

    /// <summary>POST /ticket/create — insert ticket + ticket_item in single tx</summary>
    [HttpPost("create")]
    public async Task<ResultMessage<TicketDto>> CreateAsync(
        [FromBody] CreateTicketFullRequest request,
        CancellationToken ct)
    {
        if (request?.Ticket is null || request.Detail is null)
            return ResultMessage<TicketDto>.Error(500, "ticket and detail are required");

        try
        {
            var data = await _service.CreateAsync(request.Ticket, request.Detail, ct);
            return ResultMessage<TicketDto>.Data(data);
        }
        catch (ArgumentException ex)
        {
            _log.LogWarning("Validation error: {Msg}", ex.Message);
            // Preserve the Java 500-for-validation quirk (noted in TASK-011 spec).
            return ResultMessage<TicketDto>.Error(500, ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating ticket");
            return ResultMessage<TicketDto>.Error(500, "Failed to create ticket");
        }
    }

    /// <summary>GET /ticket/{id} — detail; 404 when missing</summary>
    [HttpGet("{ticketId:long}")]
    public async Task<ResultMessage<TicketDto>> GetByIdAsync(long ticketId, CancellationToken ct)
    {
        try
        {
            var data = await _service.GetByIdAsync(ticketId, ct);
            return ResultMessage<TicketDto>.Data(data);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error fetching ticket {Id}", ticketId);
            return ResultMessage<TicketDto>.Error(500, ex.Message);
        }
    }

    /// <summary>PUT /ticket/{id} — update name/description/time (currently mirror of Java no-op)</summary>
    [HttpPut("{ticketId:long}")]
    public async Task<ResultMessage<TicketDto?>> UpdateAsync(
        long ticketId,
        [FromBody] UpdateTicketRequest updateRequest,
        CancellationToken ct)
    {
        try
        {
            var data = await _service.UpdateAsync(ticketId, updateRequest, ct);
            return ResultMessage<TicketDto?>.Data(data);
        }
        catch (ArgumentException ex)
        {
            _log.LogWarning("Validation error: {Msg}", ex.Message);
            return ResultMessage<TicketDto?>.Error(500, ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating ticket {Id}", ticketId);
            return ResultMessage<TicketDto?>.Error(500, ex.Message);
        }
    }

    /// <summary>PUT /ticket/{id}/active — set status=1</summary>
    [HttpPut("{ticketId:long}/active")]
    public async Task<ResultMessage<TicketDto>> ActivateAsync(long ticketId, CancellationToken ct)
    {
        try
        {
            var data = await _service.ActivateAsync(ticketId, ct);
            return ResultMessage<TicketDto>.Data(data);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error activating ticket {Id}", ticketId);
            return ResultMessage<TicketDto>.Error(500, ex.Message);
        }
    }

    /// <summary>PUT /ticket/{id}/inactive — set status=0</summary>
    [HttpPut("{ticketId:long}/inactive")]
    public async Task<ResultMessage<TicketDto>> DeactivateAsync(long ticketId, CancellationToken ct)
    {
        try
        {
            var data = await _service.DeactivateAsync(ticketId, ct);
            return ResultMessage<TicketDto>.Data(data);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deactivating ticket {Id}", ticketId);
            return ResultMessage<TicketDto>.Error(500, ex.Message);
        }
    }

    /// <summary>DELETE /ticket/{id} — soft delete (status=2)</summary>
    [HttpDelete("{ticketId:long}")]
    public async Task<ResultMessage<string>> DeleteAsync(long ticketId, CancellationToken ct)
    {
        try
        {
            await _service.DeleteAsync(ticketId, ct);
            return ResultMessage<string>.Data("Ticket deleted successfully");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting ticket {Id}", ticketId);
            return ResultMessage<string>.Error(500, ex.Message);
        }
    }
}
