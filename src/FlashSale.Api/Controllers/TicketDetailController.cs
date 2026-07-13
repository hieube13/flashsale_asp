using FlashSale.Application.Services;
using FlashSale.Contracts.Dto;
using Microsoft.AspNetCore.Mvc;

namespace FlashSale.Api.Controllers;

/// <summary>
/// TicketDetail endpoint — mirrors Java TicketDetailController (2 endpoints + 1 ping).
/// </summary>
[ApiController]
[Route("ticket")]
public sealed class TicketDetailController : ControllerBase
{
    private readonly ITicketDetailAppService _service;
    private readonly ILogger<TicketDetailController> _log;

    public TicketDetailController(ITicketDetailAppService service, ILogger<TicketDetailController> log)
    {
        _service = service;
        _log = log;
    }

    /// <summary>GET /ticket/ping/java — sleep 1s then return "OK" (parity with Java)</summary>
    [HttpGet("ping/java")]
    public async Task<IActionResult> PingAsync(CancellationToken ct)
    {
        await Task.Delay(1000, ct);
        return Ok(new { status = "OK" });
    }

    /// <summary>GET /ticket/{ticketId}/detail/{detailId}?version=… — reads L1+L2 cache</summary>
    [HttpGet("{ticketId:long}/detail/{detailId:long}")]
    public async Task<ResultMessage<TicketDetailDto>> GetDetailAsync(
        long ticketId,
        long detailId,
        [FromQuery(Name = "version")] long? version,
        CancellationToken ct)
    {
        try
        {
            var data = await _service.GetByIdAsync(detailId, version, ct);
            return ResultMessage<TicketDetailDto>.Data(data);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error fetching TicketDetail {Id}", detailId);
            return ResultMessage<TicketDetailDto>.Error(500, ex.Message);
        }
    }

    /// <summary>GET /ticket/{ticketId}/detail/{detailId}/order — Java returns plain bool</summary>
    [HttpGet("{ticketId:long}/detail/{detailId:long}/order")]
    public async Task<bool> OrderByUserAsync(long ticketId, long detailId, CancellationToken ct)
        => await _service.OrderByUserAsync(detailId, ct);
}
