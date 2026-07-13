# TASK-009 — api_foundation

| Field | Value |
|-------|-------|
| Status | ✅ done |
| Branch | — |
| Module | api |
| Phase | 0 — Scaffold |
| Commit | — |
| Completed | 2026-07-13 |

## Mục tiêu

`Program.cs` dựng DI wiring, Kestrel bind port 5080, Serilog, Prometheus middleware, /health, /metrics. Stub implementations cho mọi service interface (throw `NotImplementedException`).

## Tệp Java nguồn (chỉ đọc)

- `xxxx-start/.../StartApplication.java` — @SpringBootApplication entrypoint
- `xxxx-start/.../application.yml` — server.port=1122, tomcat tuning

## File .NET đích (đã tạo)

- `src/FlashSale.Api/Program.cs` — DI wiring, Serilog, Kestrel :5080, /metrics, /health
- `src/FlashSale.Api/Stubs.cs` — 9 stub services
- `src/FlashSale.Api/Workers/KafkaOrderConsumerWorker.cs` — BackgroundService
- `src/FlashSale.Api/Workers/OutboxPublisherWorker.cs` — BackgroundService

## DI registrations

```csharp
builder.WebHost.ConfigureKestrel(opts => opts.ListenAnyIP(5080));
builder.Host.UseSerilog(...);

builder.Services.AddDbContext<FlashSaleDbContext>(opts =>
    opts.UseMySql(mysqlConn, ServerVersion.AutoDetect(mysqlConn)));
builder.Services.Configure<MySqlOptions>(o => o.ConnectionString = mysqlConn);
builder.Services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();

builder.Services.AddSingleton<IConnectionMultiplexer>(...);
builder.Services.AddSingleton<IRedisInfrasService, RedisInfrasService>();
builder.Services.AddSingleton<IStockOrderCacheService, StockOrderCacheService>();

builder.Services.AddSingleton<IDistributedLockProvider, RedLockDistributedLockProvider>();

builder.Services.Configure<KafkaOptions>(...);
builder.Services.AddSingleton<IKafkaOrderProducer, KafkaOrderProducer>();
builder.Services.AddHostedService<KafkaOrderConsumerWorker>();
builder.Services.AddHostedService<OutboxPublisherWorker>();

builder.Services.AddSingleton<IVnPayGatewayService, VnPayGatewayService>();

// 9 application service stubs (each replaced per TASK-011..020)
builder.Services.AddScoped<ITicketAppService, TicketAppServiceStub>();
// ... etc

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
```

## Checklist

- [x] Kestrel on port 5080
- [x] Serilog with console sink + config reader
- [x] prometheus-net `/metrics` middleware
- [x] /health endpoint
- [x] 9 application services registered as stubs
- [x] 2 BackgroundServices (consumer, outbox publisher) registered
- [x] Build pass

## Verification

```powershell
dotnet build FlashSale.slnx
dotnet run --project src/FlashSale.Api --launch-profile http
# In another terminal:
curl http://localhost:5080/health
# {"status":"ok","at":"..."}
```