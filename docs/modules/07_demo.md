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
| POST | `/api/v1/secure/data` | Signature-protected echo | Custom middleware |
| GET | `/api/v1/secure/info` | Signature-protected info | Custom middleware |
| GET | `/ticket/ping/java` | Sleep 1s, return `{status: OK}` | Parity with Java |

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

Java: filter-based `InvalidSignatureException`. .NET: middleware `SignatureAuthMiddleware` reads HMAC signature from header `X-Signature`, validates against request body.

## Tasks

- **TASK-020**: booking_demo — port all demo endpoints

## Known quirks

- Java uses `RestTemplate` → .NET uses `IHttpClientFactory` + `HttpClient`
- Java uses Resilience4j → .NET uses Polly v8
- Java uses `SecureRandom` for product ID selection → .NET uses `Random.Shared`