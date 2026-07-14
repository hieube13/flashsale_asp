# Module 07 — Demo (Hi + SecureApi + ping/java)

## Java → .NET

| Java | .NET |
|------|------|
| `com.xxxx.ddd.controller.http.HiController` | `FlashSale.Api.Controllers.HiController` |
| `com.xxxx.ddd.controller.http.SecureApiController` | `FlashSale.Api.Controllers.SecureApiController` |
| `com.xxxx.ddd.controller.http.TicketDetailController` (ping/java endpoint) | `FlashSale.Api.Controllers.TicketDetailController` (existing) |
| `com.xxxx.ddd.application.service.event.EventAppService` | `FlashSale.Application.Services.IEventAppService` |

## Endpoints

| Method | Route | Behaviour | Notes |
|--------|-------|-----------|-------|
| GET | `/hello/hi` | Returns "Hi" | RateLimit backendA (2/10s) |
| GET | `/hello/hi/v1` | Returns "Ho" | RateLimit backendB (5/10s) |
| GET | `/hello/circuit/breaker` | Calls fakestoreapi.com/products/{1..20} | CircuitBreaker checkRandom |
| GET | `/api/v1/secure/info` | Static `{status:"success", message:"This is secure information."}` | No middleware (dev-mode passthrough — Java also has no working filter, KNOWN_DIFFERENCES §28) |
| GET | `/api/v1/secure/unauthorized` | HTTP 401 `{status:"unauthorized"}` | .NET-extra demo route (sub-route mode, not in Java) |
| GET | `/api/v1/secure/forbidden` | HTTP 403 `{status:"forbidden"}` | .NET-extra demo route |
| GET | `/api/v1/secure/slow` | HTTP 200 after 2 s `Task.Delay` | .NET-extra demo route (slow-call threshold) |
| GET | `/api/v1/secure/throw` | Throws `InvalidOperationException` | .NET-extra demo route (exception simulator) |
| GET | `/ticket/ping/java` | Sleep 1s, return `{status: OK}` | Parity with Java (done in TASK-011) |

## Rate limiting (.NET 8 built-in)

```csharp
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("backendA", opt =>
    {
        opt.PermitLimit = 2;
        opt.Window = TimeSpan.FromSeconds(10);
        opt.QueueLimit = 0;
    });
    o.AddFixedWindowLimiter("backendB", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromSeconds(10);
        opt.QueueLimit = 0;
    });
});

app.MapGet("/hello/hi", ...).RequireRateLimiting("backendA");
app.MapGet("/hello/hi/v1", ...).RequireRateLimiting("backendB");
```

Fallback body: `"Too many request"` (matches Java `@RateLimiter` fallback).

## Circuit breaker (Polly v8)

```csharp
builder.Services.AddResiliencePipeline("checkRandom", b =>
{
    b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        MinimumThroughput = 5,
        SamplingDuration = TimeSpan.FromSeconds(10),
        BreakDuration = TimeSpan.FromSeconds(5)
    });
});

app.MapGet("/hello/circuit/breaker", async (HttpClient http) =>
{
    return await pipeline.ExecuteAsync(async ct =>
    {
        var id = Random.Shared.Next(1, 21);
        return await http.GetFromJsonAsync<object>($"https://fakestoreapi.com/products/{id}", ct);
    });
});
```

Fallback: `"Service fakestoreapi Error!"` (matches Java fallback).

## Secure API

Java has a class `InvalidSignatureException` (`xxxx-controller/.../controller/exception/InvalidSignatureException.java`) but **no filter or interceptor actually uses it** — both `/api/v1/secure/info` and `/api/v1/secure/data` are unprotected in Java.

**Decision (TASK-020 Q4 — user approved dev-mode passthrough):** .NET keeps both Java endpoints in **dev-mode passthrough** (no signature middleware). All 6 secure endpoints return raw `{status, message, …}` — **NO `ResultMessage<T>` wrapper**.

## Tasks

- **TASK-020**: booking_demo — port all demo endpoints (done 2026-07-14)

## Known quirks

- Java uses `RestTemplate` → .NET uses `IHttpClientFactory` + `HttpClient`
- Java uses Resilience4j → .NET uses Polly v8 (AddResiliencePipeline extension via `Microsoft.Extensions.Http.Resilience 8.10.0`)
- Java uses `SecureRandom` for product ID selection → .NET uses `Random.Shared.Next(1, 21)` (1..20 inclusive)
- Java `EventAppService.sayHi(who)` ignores input → .NET `EventAppServiceImpl.SayHi(name)` returns `"Hi Infrastructure"` literal for ANY input (KNOWN_DIFFERENCES §27)
- Java `@RateLimiter` rejects with `"Too many request"` fallback → .NET uses `Microsoft.AspNetCore.RateLimiting` fixed-window with `RejectionStatusCode=429`. Java's String-fallback body is not produced; the .NET equivalent is HTTP 429 with empty body.
- Java `SecureApiController` has 2 raw endpoints (no mode-switching) → .NET keeps those 2 verbatim + adds 4 sub-routes for circuit-breaker smoke (KNOWN_DIFFERENCES §28)