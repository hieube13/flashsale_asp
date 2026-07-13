using FlashSale.Application.Services;
using FlashSale.Contracts.Dto;
using Microsoft.AspNetCore.Mvc;

namespace FlashSale.Api.Controllers;

/// <summary>
/// Order read slice — mirrors Java OrderController (TASK-012).
/// Three list endpoints backed by the monthly shard
/// <c>ticket_order_{yyyyMM}</c>. The Java controller signature takes
/// <c>{userId}</c> but the underlying service ignores it on the read path
/// (only the orderNumber filter matters — the userId is included in the
/// route purely because Java puts it there for parity).
/// All endpoints wrap the response in <see cref="ResultMessage{T}"/>
/// (matches Java ResultUtil convention).
/// </summary>
[ApiController]
[Route("order")]
public sealed class OrderController : ControllerBase
{
    private readonly ITicketOrderAppService _service;
    private readonly ILogger<OrderController> _log;

    public OrderController(ITicketOrderAppService service, ILogger<OrderController> log)
    {
        _service = service;
        _log = log;
    }

    /// <summary>GET /order/{userId}/{orderNumber} — look up a single order by id.</summary>
    [HttpGet("{userId:long}/{orderNumber}")]
    public async Task<ResultMessage<TicketOrderDto?>> GetByOrderNumberAsync(
        long userId,
        string orderNumber,
        [FromQuery] string? yearMonth,
        CancellationToken ct)
    {
        try
        {
            // Java ignores the yearMonth arg when order_number is supplied —
            // the year-month is derived from the trailing timestamp segment
            // of the order number itself (see OrderDeductionDomainService).
            // We accept the query string for parity but do not forward it.
            var ym = string.IsNullOrEmpty(yearMonth) ? "000000" : yearMonth;
            var dto = await _service.FindByOrderNumberAsync(ym, orderNumber, ct);
            if (dto is null)
                return ResultMessage<TicketOrderDto?>.Error(404, $"Order {orderNumber} not found");
            _log.LogInformation("Order {OrderNumber} (user={UserId}) resolved", orderNumber, userId);
            return ResultMessage<TicketOrderDto?>.Data(dto);
        }
        catch (ArgumentException ex)
        {
            _log.LogWarning("findByOrderNumber validation: {Msg}", ex.Message);
            return ResultMessage<TicketOrderDto?>.Error(400, ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "findByOrderNumber failed for {OrderNumber}", orderNumber);
            return ResultMessage<TicketOrderDto?>.Error(500, "Internal error");
        }
    }

    /// <summary>GET /order/{userId}/list — full dump (limit 1000) for the given shard.</summary>
    [HttpGet("{userId:long}/list")]
    public async Task<ResultMessage<IReadOnlyList<TicketOrderDto>>> ListByUserAsync(
        long userId,
        [FromQuery] string yearMonth,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(yearMonth))
                return ResultMessage<IReadOnlyList<TicketOrderDto>>.Error(400, "yearMonth query param is required");
            var data = await _service.FindAllAsync(yearMonth, ct);
            _log.LogInformation("Order list user={UserId} yearMonth={YearMonth} count={Count}", userId, yearMonth, data.Count);
            return ResultMessage<IReadOnlyList<TicketOrderDto>>.Data(data);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "findAll failed for user={UserId} yearMonth={YearMonth}", userId, yearMonth);
            return ResultMessage<IReadOnlyList<TicketOrderDto>>.Error(500, "Internal error");
        }
    }

    /// <summary>GET /order/{userId}/list/page — cursor paged reads (id DESC).</summary>
    [HttpGet("{userId:long}/list/page")]
    public async Task<ResultMessage<PagedOrdersDto>> ListPageByUserAsync(
        long userId,
        [FromQuery] string yearMonth,
        [FromQuery] long lastId,
        [FromQuery] int limit,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(yearMonth))
                return ResultMessage<PagedOrdersDto>.Error(400, "yearMonth query param is required");
            var data = await _service.FindPageAsync(yearMonth, lastId, limit, ct);
            _log.LogInformation("Order page user={UserId} yearMonth={YearMonth} lastId={LastId} count={Count}",
                userId, yearMonth, lastId, data.Items.Count);
            return ResultMessage<PagedOrdersDto>.Data(data);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "findPage failed for user={UserId} yearMonth={YearMonth}", userId, yearMonth);
            return ResultMessage<PagedOrdersDto>.Error(500, "Internal error");
        }
    }

    // ============== TASK-013: order CAS slice ==============

    /// <summary>
    /// POST /order/cas — primary order endpoint.
    /// Wraps Redis Lua atomic decrement + DB safety net + order-row insert.
    /// Java parity: <c>placeOrderCAS(Long ticketId, int quantity)</c>.
    /// </summary>
    [HttpPost("cas")]
    public async Task<ResultMessage<PlaceOrderResponse>> PlaceOrderCasAsync(
        [FromQuery] long ticketId,
        [FromQuery] int quantity,
        CancellationToken ct)
    {
        try
        {
            if (quantity <= 0)
                return ResultMessage<PlaceOrderResponse>.Error(400, "quantity must be positive");
            var resp = await _service.PlaceOrderCasAsync(ticketId, quantity, ct);
            return ResultMessage<PlaceOrderResponse>.Data(resp, resp.Message ?? (resp.Success ? "OK" : "Failed"));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "placeOrderCAS failed for ticketId={TicketId} qty={Qty}", ticketId, quantity);
            return ResultMessage<PlaceOrderResponse>.Error(500, "Internal error");
        }
    }

    /// <summary>
    /// GET /order/{ticketId}/{quantity}/order — demo route mirroring Java's
    /// <c>OrderController.placeOrderCAS</c> GET variant. Returns the same
    /// <see cref="PlaceOrderResponse"/> envelope.
    /// </summary>
    [HttpGet("{ticketId:long}/{quantity:int}/order")]
    public async Task<ResultMessage<PlaceOrderResponse>> PlaceOrderCasGetAsync(
        long ticketId,
        int quantity,
        CancellationToken ct)
    {
        try
        {
            var resp = await _service.PlaceOrderCasAsync(ticketId, quantity, ct);
            return ResultMessage<PlaceOrderResponse>.Data(resp, resp.Message ?? (resp.Success ? "OK" : "Failed"));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "placeOrderCAS GET failed for ticketId={TicketId} qty={Qty}", ticketId, quantity);
            return ResultMessage<PlaceOrderResponse>.Error(500, "Internal error");
        }
    }

    /// <summary>
    /// GET /order/{ticketId}/{quantity}/cas — demo route for the legacy
    /// L3 decrement path. Returns raw <c>bool</c> wrapped in
    /// <see cref="ResultMessage{T}"/> so FE clients see a JSON envelope.
    /// </summary>
    [HttpGet("{ticketId:long}/{quantity:int}/cas")]
    public async Task<ResultMessage<bool>> DecreaseStockLevel3CasAsync(
        long ticketId,
        int quantity,
        CancellationToken ct)
    {
        try
        {
            var ok = await _service.DecreaseStockLevel3CasAsync(ticketId, quantity, ct);
            return ResultMessage<bool>.Data(ok, ok ? "OK" : "FAILED");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "L3 CAS failed for ticketId={TicketId} qty={Qty}", ticketId, quantity);
            return ResultMessage<bool>.Error(500, "Internal error");
        }
    }

    /// <summary>
    /// GET /order/{ticketId}/{quantity}/{userId}/queued — entry-point for the
    /// Kafka-based MQ producer path that ships in TASK-015. Currently returns
    /// <see cref="NotImplementedException"/>-style 501 with a clear message so
    /// FE clients can detect it.
    /// </summary>
    [HttpGet("{ticketId:long}/{quantity:int}/{userId:long}/queued")]
    public Task<ResultMessage<bool>> DecreaseStockQueueAsync(
        long ticketId,
        int quantity,
        long userId,
        CancellationToken ct)
    {
        _log.LogInformation("queued endpoint hit ticket={TicketId} qty={Qty} user={UserId} — not yet implemented (TASK-015)",
            ticketId, quantity, userId);
        return Task.FromResult(ResultMessage<bool>.Error(501, "TASK-015: order MQ producer not implemented"));
    }

    // ============== TASK-014: order cancel slice ==============

    /// <summary>
    /// PUT /order/{userId}/{orderNumber}/cancel — cancel an order under a
    /// distributed lock. Mirrors Java TicketOrderController.cancelOrder
    /// (<c>@PutMapping("/{userId}/{orderNumber}/cancel")</c>) and Java
    /// TicketOrderAppServiceImpl.cancelOrder lines 439-511.
    /// <para>
    /// HTTP status is always <c>200</c>; the body's <c>success</c> field
    /// indicates whether the cancel succeeded (lock acquired, ownership
    /// matched, status flipped to CANCELLED, stock restored). This matches
    /// <c>KNOWN_DIFFERENCES.md</c> §4 (success-failure both return 200).
    /// </para>
    /// </summary>
    [HttpPut("{userId:long}/{orderNumber}/cancel")]
    public async Task<ResultMessage<bool>> CancelOrderAsync(
        long userId,
        string orderNumber,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(orderNumber))
                return ResultMessage<bool>.Error(400, "orderNumber is required");
            var ok = await _service.CancelOrderAsync(userId, orderNumber, ct);
            if (ok)
                return ResultMessage<bool>.Data(true, "Cancel order successfully");
            // Either lock-busy, order not found, ownership mismatch, or status
            // update failure. Keep status 200 to match Java semantics.
            return ResultMessage<bool>.Data(false, "Cancel order failed");
        }
        catch (ArgumentException ex)
        {
            _log.LogWarning("cancelOrder validation: {Msg}", ex.Message);
            return ResultMessage<bool>.Error(400, ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "cancelOrder failed for userId={UserId} orderNumber={OrderNumber}", userId, orderNumber);
            return ResultMessage<bool>.Error(500, "Internal error");
        }
    }
}
