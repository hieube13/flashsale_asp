using System.Text.Json;
using FlashSale.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;

namespace FlashSale.Api.Controllers;

/// <summary>
/// Hi controller — TASK-020 (port of Java <c>HiController</c>).
///
/// Mirrors Java endpoints (lines 15-52 of <c>HiController.java</c>):
///   • <c>GET /hello/hi</c>             → rate-limited by <c>backendA</c> (2 req / 10 s).
///   • <c>GET /hello/hi/v1</c>          → rate-limited by <c>backendB</c> (5 req / 10 s).
///   • <c>GET /hello/circuit/breaker</c> → wraps an external call to
///       <c>https://fakestoreapi.com/products/{id}</c> in Polly's <c>checkRandom</c>
///       circuit-breaker pipeline (FailureRatio = 0.5, MinThroughput = 5, 10 s sampling,
///       5 s open-state). Fallback body = <c>"Service fakestoreapi Error!"</c>.
/// All endpoints route through <see cref="IEventAppService.SayHi(string)"/> for the greeting —
/// the external URL call is the source of the circuit-breaker demo.
/// </summary>
[ApiController]
[Route("hello")]
public sealed class HiController : ControllerBase
{
    public const string FALLBACK_BODY = "Service fakestoreapi Error!";
    public const string CHECK_RANDOM_PIPELINE = "checkRandom";

    private readonly IHttpClientFactory _httpFactory;
    private readonly IEventAppService _events;
    private readonly ResiliencePipelineProvider<string> _pipelines;
    private readonly ILogger<HiController> _log;

    public HiController(
        IHttpClientFactory httpFactory,
        IEventAppService events,
        ResiliencePipelineProvider<string> pipelines,
        ILogger<HiController> log)
    {
        _httpFactory = httpFactory;
        _events = events;
        _pipelines = pipelines;
        _log = log;
    }

    /// <summary>Java HiController.java:16-22 — returns the literal "Hi".</summary>
    [HttpGet("hi")]
    [EnableRateLimiting("backendA")]
    public string SayHi() => _events.SayHi("Hi");

    /// <summary>Java HiController.java:24-30 — same literal under backendB policy.</summary>
    [HttpGet("hi/v1")]
    [EnableRateLimiting("backendB")]
    public string SayHiV1() => _events.SayHi("Ho");

    /// <summary>
    /// Java HiController.java:32-52 — calls <c>fakestoreapi.com/products/{1..20}</c>
    /// via <c>RestTemplate</c>, wrapped in <c>@CircuitBreaker(name="checkRandom", …)</c>.
    /// Returns the JSON body verbatim, or <see cref="FALLBACK_BODY"/> on break/open.
    /// </summary>
    [HttpGet("circuit/breaker")]
    public async Task<string> CircuitBreakerAsync(CancellationToken ct)
    {
        var id = Random.Shared.Next(1, 21); // 1..20 inclusive
        var url = $"https://fakestoreapi.com/products/{id}";

        var pipeline = _pipelines.GetPipeline(CHECK_RANDOM_PIPELINE);
        try
        {
            return await pipeline.ExecuteAsync(async token =>
            {
                var client = _httpFactory.CreateClient();
                using var resp = await client.GetAsync(url, token);
                resp.EnsureSuccessStatusCode();
                var body = await resp.Content.ReadAsStringAsync(token);
                // Java returns the raw product JSON; trim heavy whitespace for parity in tests.
                return JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(body));
            }, ct);
        }
        catch (BrokenCircuitException)
        {
            _log.LogWarning("[Hi/circuit/breaker] Circuit OPEN — returning fallback body");
            return FALLBACK_BODY;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[Hi/circuit/breaker] call failed — returning fallback body");
            return FALLBACK_BODY;
        }
    }
}