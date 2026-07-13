using FlashSale.Application.Services;
using FlashSale.Contracts.Dto;
using FlashSale.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace FlashSale.Api.Controllers;

/// <summary>
/// OrderMQ controller — async place-order endpoints.
/// Mirrors Java <c>OrderMQController</c> (<c>@RequestMapping("/order/mq")</c>).
/// <para>
/// POST /order/mq — Lua pre-deduct + transactional outbox write → return token (PENDING)
///                or {success:false, code, message} on TICKET_NOT_FOUND / OUT_OF_STOCK /
///                PRICE_NOT_FOUND / INTERNAL_ERROR.
/// GET  /order/mq/status/{token} — poll order_queue status (0=PENDING, 1=SUCCESS, 2=FAILED).
/// </para>
/// <para>
/// HTTP status is always 200; success/failure is signalled in the body
/// (matches Java ResultUtil convention and KNOWN_DIFFERENCES.md §4).
/// </para>
/// </summary>
[ApiController]
[Route("order/mq")]
public sealed class OrderMQController : ControllerBase
{
    private readonly IOrderMqAppService _service;
    private readonly ILogger<OrderMQController> _log;

    public OrderMQController(IOrderMqAppService service, ILogger<OrderMQController> log)
    {
        _service = service;
        _log = log;
    }

    /// <summary>POST /order/mq — enqueue an order.</summary>
    [HttpPost]
    public async Task<ResultMessage<PlaceOrderResponse>> PlaceOrderMqAsync(
        [FromBody] PlaceOrderMqRequest request,
        CancellationToken ct)
    {
        try
        {
            if (request.Quantity <= 0)
                return ResultMessage<PlaceOrderResponse>.Error(400, "quantity must be positive");
            _log.LogInformation("OrderMQController.placeOrderMQ ticketId={TicketId} qty={Qty}",
                request.TicketId, request.Quantity);
            var queue = await _service.PlaceOrderMqAsync(request.TicketId, request.Quantity, ct);
            var resp = ToResponse(queue);
            return ResultMessage<PlaceOrderResponse>.Data(resp, resp.Message ?? (resp.Success ? "Queued" : "Failed"));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "placeOrderMQ unhandled error ticketId={TicketId}", request.TicketId);
            return ResultMessage<PlaceOrderResponse>.Data(
                PlaceOrderResponse.Failed("SERVER_ERROR", "Lỗi hệ thống, vui lòng thử lại"));
        }
    }

    /// <summary>GET /order/mq/status/{token} — poll status.</summary>
    [HttpGet("status/{token}")]
    public async Task<ResultMessage<OrderQueue>> GetOrderStatusAsync(string token, CancellationToken ct)
    {
        try
        {
            var q = await _service.GetOrderStatusAsync(token, ct);
            return q is null
                ? ResultMessage<OrderQueue>.Error(404, $"Order {token} not found")
                : ResultMessage<OrderQueue>.Data(q);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "getOrderStatus failed token={Token}", token);
            return ResultMessage<OrderQueue>.Error(500, "Internal error");
        }
    }

    /// <summary>
    /// Maps an OrderQueue (from the service) to a PlaceOrderResponse for the FE.
    /// Mirrors Java <c>OrderMQController.toResponse</c> (lines 49-58).
    /// <para>OrderQueue.Status values: 0=PENDING, 1=SUCCESS, 2=FAILED.</para>
    /// </summary>
    private static PlaceOrderResponse ToResponse(OrderQueue queue)
    {
        if (queue.Status == 2)
        {
            // Parse "CODE: message" from Message field (Java line 53).
            var raw = !string.IsNullOrEmpty(queue.Message) ? queue.Message : "ERROR: Server error";
            string code;
            string fullMsg;
            var idx = raw.IndexOf(':');
            if (idx > 0)
            {
                code = raw[..idx].Trim();
                fullMsg = raw[(idx + 1)..].Trim();
            }
            else
            {
                code = "ERROR";
                fullMsg = raw;
            }
            return PlaceOrderResponse.Failed(code, fullMsg);
        }
        // Status 0 (PENDING) or 1 (SUCCESS) — both surface as queued.
        return PlaceOrderResponse.Ok(queue.Token);
    }
}