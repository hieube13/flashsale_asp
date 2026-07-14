using Microsoft.AspNetCore.Mvc;

namespace FlashSale.Api.Controllers;

/// <summary>
/// SecureApi controller — TASK-020 (port of Java <c>SecureApiController</c>).
///
/// <para>
/// Java exposes exactly TWO raw echo endpoints under <c>/api/v1/secure</c>
/// (<c>SecureApiController.java:10-21</c>) — NO mode-switching, NO <c>Thread.sleep</c>,
/// NO <c>@PreAuthorize</c>. The class is named <c>SecureApi</c> but in this
/// version of the Java source there is no signature filter or auth interceptor
/// registered (only a stub <c>InvalidSignatureException</c> class exists).
/// </para>
/// <para>
/// <b>.NET decision (Q3, user approved "sub-route"):</b> keep the two raw endpoints
/// verbatim, but ADD three extra endpoints to simulate circuit-breaker scenarios.
/// All endpoints return raw <c>{status, message, …}</c> (no <see cref="ResultMessage{T}"/>)
/// and pass through with no middleware (dev-mode passthrough — Q4).
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/secure")]
public sealed class SecureApiController : ControllerBase
{
    public const string INFO_RESPONSE = "This is secure information.";

    /// <summary>GET /api/v1/secure/info — Java SecureApiController.java:18-21</summary>
    [HttpGet("info")]
    public IActionResult GetInfo()
        => Ok(new { status = "success", message = INFO_RESPONSE });

    /// <summary>POST /api/v1/secure/data — Java SecureApiController.java:12-16</summary>
    [HttpPost("data")]
    public IActionResult PostData([FromBody] object? payload)
        => Ok(new
        {
            status = "success",
            message = "Secure data processed!",
            receivedPayload = payload,
        });

    // ── Extra demo endpoints (TASK-020 Q3 — sub-route mode-switching) ──────────
    // These do NOT exist in Java. They are extra endpoints for circuit-breaker /
    // exception-handling smoke testing. All return raw JSON, no auth.

    /// <summary>Always-401 endpoint — useful to test client retry policies.</summary>
    [HttpGet("unauthorized")]
    public IActionResult GetUnauthorized()
        => StatusCode(401, new { status = "unauthorized", message = "Invalid credentials" });

    /// <summary>Always-403 endpoint.</summary>
    [HttpGet("forbidden")]
    public IActionResult GetForbidden()
        => StatusCode(403, new { status = "forbidden", message = "Access denied" });

    /// <summary>Always-200 after a 2-second sleep — useful to test slow-call thresholds.</summary>
    [HttpGet("slow")]
    public async Task<IActionResult> GetSlowAsync(CancellationToken ct)
    {
        await Task.Delay(2000, ct);
        return Ok(new { status = "success", message = "slow OK", elapsedMs = 2000 });
    }

    /// <summary>Always throws — exercises the global exception filter / Polly.</summary>
    [HttpGet("throw")]
    public IActionResult GetThrow()
        => throw new InvalidOperationException("SecureApi demo forced exception");
}