# TASK-020 — booking_demo

| Field | Value |
|-------|-------|
| Status | ✅ done |
| Branch | `f_task_020_booking_demo` |
| Module | booking |
| Phase | 1 — Feature port |
| Commit | — |
| Completed | 2026-07-14 |

## Mục tiêu

Port Booking endpoint + các demo controller (Hi, SecureApi, ticket/ping/java) để hoàn tất parity.

## Tệp Java nguồn (chỉ đọc)

- `xxxx-controller/.../controller/http/BookingController.java`
- `xxxx-controller/.../controller/http/HiController.java` — RateLimiter + CircuitBreaker
- `xxxx-controller/.../controller/http/SecureApiController.java`
- `xxxx-controller/.../controller/http/TicketDetailController.java:23-48` — `/ticket/ping/java`
- `xxxx-application/.../application/service/booking/BookingAppService.java` + `Impl`
- `xxxx-application/.../application/service/event/EventAppService.java` + `Impl`

## File .NET đích (sẽ tạo)

- `src/FlashSale.Application/Services/Implementations/BookingAppServiceImpl.cs`
- `src/FlashSale.Application/Services/Implementations/EventAppServiceImpl.cs`
- `src/FlashSale.Infrastructure/Persistence/Repositories/BookingRepositoryImpl.cs`
- `src/FlashSale.Api/Controllers/BookingController.cs`
- `src/FlashSale.Api/Controllers/HiController.cs`
- `src/FlashSale.Api/Controllers/SecureApiController.cs`
- `src/FlashSale.Api/Filter/SignatureAuthFilter.cs` (or middleware) for `/api/v1/secure/*`

## Endpoints

| Method | Route | Behaviour |
|--------|-------|-----------|
| POST | `/api/bookings` | Create Booking (Java has full impl; just port) |
| GET | `/hello/hi` | Return "Hi" with rate limit (2/10s) |
| GET | `/hello/hi/v1` | Return "Ho" with rate limit (5/10s) |
| GET | `/hello/circuit/breaker` | Call fakestoreapi.com with circuit breaker |
| POST | `/api/v1/secure/data` | Signature-auth (custom middleware) |
| GET | `/api/v1/secure/info` | Signature-auth |
| GET | `/ticket/ping/java` | Sleep 1s, return OK (parity with Java) |

## RateLimiter in .NET 8

Use built-in `Microsoft.AspNetCore.RateLimiting`:

```csharp
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("backendA", opt =>
    {
        opt.PermitLimit = 2;
        opt.Window = TimeSpan.FromSeconds(10);
        opt.QueueLimit = 0;
    });
    // ...
});

app.MapGet("/hello/hi", ...).RequireRateLimiting("backendA");
```

## CircuitBreaker in .NET 8

Use Polly v8 ResiliencePipeline:

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
```

## Acceptance criteria

- [ ] All 7 endpoints return identical JSON to Java
- [ ] Rate limiter kicks in at correct threshold (2 and 5)
- [ ] Circuit breaker opens after 50% failure of 5+ calls
- [ ] Signature middleware allows requests with valid signature, rejects others
- [ ] `/ticket/ping/java` sleeps exactly 1s
- [ ] Unit + integration tests

## Verification

```powershell
dotnet build FlashSale.slnx
dotnet test tests/FlashSale.UnitTests --filter "FullyQualifiedName~Booking|FullyQualifiedName~Hi|FullyQualifiedName~Secure"

# Smoke
curl -X POST http://localhost:5080/api/bookings -H "Content-Type: application/json" \
  -d '{"ticketId":4,"quantity":2}'
curl http://localhost:5080/hello/hi
curl http://localhost:5080/ticket/ping/java
```

## Suggested commit

```
[TASK-020] booking_demo: booking stub + hi rate-limit + circuit breaker + secure signature + ping
```