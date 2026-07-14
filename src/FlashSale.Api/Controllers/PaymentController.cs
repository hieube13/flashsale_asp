using FlashSale.Application.Services;
using FlashSale.Application.Services.Implementations;
using FlashSale.Contracts.Dto;
using FlashSale.Domain.Repositories;
using FlashSale.Infrastructure.External;
using Microsoft.AspNetCore.Mvc;

namespace FlashSale.Api.Controllers;

/// <summary>
/// Payment controller — TASK-018 (port of Java <c>PaymentController</c>).
/// <para>
///   POST /payment/create — body <c>CreatePaymentRequest{userId, orderNumber, method}</c>.
///     Returns <c>ResultMessage&lt;PaymentUrlResponse&gt;</c> with <c>success:true</c>
///     and <c>data.paymentUrl</c> on success. The amount is fetched server-side from
///     <c>ticket_order_{yyyyMM}</c>; clients never specify it.
///   GET  /payment/callback/return — Return URL hit by the user's browser after a
///     successful gateway redirect. <b>Not</b> authoritative — we only render a
///     minimal status page; VNPay's actual business confirmation comes via IPN.
///   POST /payment/callback/ipn — IPN webhook. Async, server-to-server. Body is
///     <c>application/x-www-form-urlencoded</c>. Response is JSON
///     <c>{RspCode, Message}</c> per VNPay spec.
/// </para>
/// <para>
/// Status is always 200 OK on /create and /return; the IPN response is plain JSON
/// because that's what VNPay parses (HTTP status alone is not enough to stop
/// retries — VNPay treats everything as retryable unless RspCode is well-known).
/// </para>
/// </summary>
[ApiController]
[Route("payment")]
public sealed class PaymentController : ControllerBase
{
    private readonly IPaymentAppService _paymentService;
    private readonly PaymentAppServiceImpl _concrete;
    private readonly IVnPayGatewayService _gateway;
    private readonly IPaymentRepository _payments;
    private readonly ILogger<PaymentController> _log;

    public PaymentController(
        IPaymentAppService paymentService,
        PaymentAppServiceImpl concrete,
        IVnPayGatewayService gateway,
        IPaymentRepository payments,
        ILogger<PaymentController> log)
    {
        _paymentService = paymentService;
        _concrete = concrete;
        _gateway = gateway;
        _payments = payments;
        _log = log;
    }

    /// <summary>POST /payment/create — build a VNPay redirect URL.</summary>
    [HttpPost("create")]
    public async Task<ResultMessage<PaymentUrlResponse>> CreateAsync(
        [FromBody] CreatePaymentRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return ResultMessage<PaymentUrlResponse>.Error(400, "request body required");
        if (request.UserId <= 0)
            return ResultMessage<PaymentUrlResponse>.Error(400, "userId must be positive");
        if (string.IsNullOrWhiteSpace(request.OrderNumber))
            return ResultMessage<PaymentUrlResponse>.Error(400, "orderNumber required");

        try
        {
            // 1. Pull client IP — falls back to "0.0.0.0" when behind a non-trusted proxy
            //    (UseForwardedHeaders middleware is wired in Program.cs to populate this).
            var ip = ResolveClientIp();

            // 2. Recover amount server-side (Java PaymentServiceImpl line 35-37).
            var amount = await _payments.GetOrderAmountAsync(request.OrderNumber, ct);

            // 3. Mint txnRef + build VNPay URL via the gateway (HMAC-SHA512 sign here).
            var txnRef = Guid.NewGuid().ToString("N");
            var orderInfo = $"Thanh toan don hang {request.OrderNumber}";
            var url = _gateway.CreatePaymentUrl(txnRef, request.OrderNumber, amount, orderInfo, ip, ct);

            // 4. AppService handles idempotency lookup + persist (so future calls for
            //    the same (userId, orderNumber) return the EXISTING URL, not a new one).
            var finalUrl = await _concrete.BuildAndPersistPaymentAsync(
                request.UserId, request.OrderNumber, request.Method ?? "VNPAY",
                url, txnRef, amount, ct);

            return ResultMessage<PaymentUrlResponse>.Data(
                new PaymentUrlResponse { PaymentUrl = finalUrl },
                "Created");
        }
        catch (ArgumentException ex)
        {
            _log.LogWarning(ex, "CreateAsync invalid input");
            return ResultMessage<PaymentUrlResponse>.Error(400, ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateAsync unhandled");
            return ResultMessage<PaymentUrlResponse>.Error(500, "internal");
        }
    }

    /// <summary>GET /payment/callback/return — return URL hit by the user's browser.</summary>
    [HttpGet("callback/return")]
    public IActionResult ReturnUrl()
    {
        var status = Request.Query.TryGetValue("vnp_ResponseCode", out var v) && v == "00"
            ? "success" : "failed";
        return Content(
            $"<html><body><h1>Payment {status}</h1>" +
            "<p>This page confirms the redirect; final state arrives via the IPN callback.</p>" +
            "</body></html>",
            "text/html; charset=utf-8");
    }

    /// <summary>
    /// POST /payment/callback/ipn — VNPay server-to-server IPN.
    /// <para>Body is <c>application/x-www-form-urlencoded</c> (default MVC binder).</para>
    /// <para>Always returns HTTP 200 with JSON <c>{RspCode,Message}</c> per VNPay spec.</para>
    /// </summary>
    [HttpPost("callback/ipn")]
    public async Task<IActionResult> IpnAsync(CancellationToken ct)
    {
        // Read vnp_* params from the form so signature verification stays inside the
        // service (single source of truth for the signing rule).
        var vnpParams = Request.HasFormContentType
            ? (IDictionary<string, string>)Request.Form
                .Where(kv => kv.Key.StartsWith("vnp_", StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key, kv => kv.Value.ToString())
            : new Dictionary<string, string>();

        // 1. Signature gate — fail fast before taking a lock or hitting the DB.
        if (!_gateway.VerifySignature(vnpParams))
        {
            _log.LogWarning("[Payment/IPN] Signature invalid txnRef={TxnRef}",
                vnpParams.TryGetValue("vnp_TxnRef", out var tx) ? tx : "<missing>");
            return Ok(new VnPayIpnResponse("97", "Invalid Signature"));
        }

        await _paymentService.HandleCallbackAsync(vnpParams, ct);

        var response = _concrete.IPNResponse ?? new VnPayIpnResponse("99", "Unknown error");
        return Ok(response);
    }

    /// <summary>
    /// Resolve the client IP, preferring X-Forwarded-For (the chain is set up
    /// by UseForwardedHeaders in Program.cs). Falls back to "0.0.0.0" when
    /// neither header nor remote address is available.
    /// </summary>
    private string ResolveClientIp()
    {
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var xff) && !string.IsNullOrWhiteSpace(xff))
        {
            // First IP in the comma-separated list is the original client.
            var first = xff.ToString().Split(',', 2)[0].Trim();
            if (!string.IsNullOrWhiteSpace(first)) return first;
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";
    }
}

/// <summary>POST /payment/create request body.</summary>
public sealed class CreatePaymentRequest
{
    public long UserId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string? Method { get; set; }
}

/// <summary>POST /payment/create success body.</summary>
public sealed class PaymentUrlResponse
{
    public string PaymentUrl { get; set; } = string.Empty;
}
