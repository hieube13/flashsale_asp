using FlashSale.Api.Controllers;
using FlashSale.Api.Workers;
using FlashSale.Application.Services;
using FlashSale.Application.Services.Implementations;
using FlashSale.Contracts.Messages;
using FlashSale.Domain.Repositories;
using FlashSale.Domain.Services;
using FlashSale.Domain.Services.Implementations;
using FlashSale.Infrastructure.Cache;
using FlashSale.Infrastructure.Data;
using FlashSale.Infrastructure.Data.Dynamic;
using FlashSale.Infrastructure.DistributedLock;
using FlashSale.Infrastructure.External;
using FlashSale.Infrastructure.Messaging;
using FlashSale.Infrastructure.Persistence;
using FlashSale.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Prometheus;
using Serilog;
using StackExchange.Redis;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ---- Serilog ----
builder.Host.UseSerilog((ctx, lc) =>
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext()
      .WriteTo.Console());

// ---- Forwarded headers (q2 — VnPay vnp_IpAddr trusts X-Forwarded-For from nginx) ----
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // In dev, accept any proxy. In production, lock this down to known CIDR.
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// ---- Kestrel / port 5080 (Java dùng 1122) ----
builder.WebHost.ConfigureKestrel(opts =>
{
    opts.ListenAnyIP(5080);
});

// ---- DB (SQL Server) ----
var sqlConn = builder.Configuration.GetConnectionString("SqlServer")
    ?? throw new InvalidOperationException("ConnectionStrings:SqlServer missing");
builder.Services.AddDbContext<FlashSaleDbContext>(opts =>
    opts.UseSqlServer(sqlConn));
builder.Services.Configure<SqlServerOptions>(o => o.ConnectionString = sqlConn);
builder.Services.AddSingleton<IDbConnectionFactory, SqlServerConnectionFactory>();

// ---- Redis ----
var redisConn = builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis:ConnectionString missing");
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
    StackExchange.Redis.ConnectionMultiplexer.Connect(redisConn));
builder.Services.AddSingleton<IRedisInfrasService, RedisInfrasService>();
builder.Services.AddScoped<IStockOrderCacheService, StockOrderCacheService>();

// ---- Distributed lock ----
// The RedLockFactory is constructed from the existing IConnectionMultiplexer
// (single Redis box in dev). The builder lives in Infrastructure so the API
// project does not need to know about RedLockNet internals.
builder.Services.AddSingleton<RedLockNet.IDistributedLockFactory>(sp =>
    RedLockFactoryBuilder.Build(
        sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>(),
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("RedLockFactory")));
builder.Services.AddSingleton<IDistributedLockProvider, RedLockDistributedLockProvider>();

// ---- Kafka ----
builder.Services.Configure<KafkaOptions>(o =>
{
    o.BootstrapServers = builder.Configuration["Kafka:BootstrapServers"] ?? string.Empty;
});
builder.Services.AddSingleton<IKafkaOrderProducer, KafkaOrderProducer>();
builder.Services.AddHostedService<KafkaOrderConsumerWorker>();

// ---- External gateway ----
builder.Services.AddSingleton<IVnPayGatewayService, VnPayGatewayService>();

// ---- Memory cache (local tier for TicketDetailCacheService) ----
builder.Services.AddMemoryCache();

// ---- Repositories (Infrastructure) ----
builder.Services.AddScoped<ITicketRepository, TicketRepositoryImpl>();
builder.Services.AddScoped<ITicketDetailRepository, TicketDetailRepositoryImpl>();
builder.Services.AddScoped<ITickerOrderRepository, TickerOrderRepositoryImpl>();
builder.Services.AddScoped<IOrderQueueRepository, OrderQueueRepositoryImpl>();
builder.Services.AddScoped<IOutboxEventRepository, OutboxEventRepositoryImpl>();
builder.Services.AddScoped<IIdempotencyKeyRepository, IdempotencyKeyRepositoryImpl>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepositoryImpl>();
builder.Services.AddScoped<IEmployeeBitSetService, EmployeeBitSetService>();
builder.Services.AddScoped<IBookingRepository, BookingRepositoryImpl>();

// ---- Domain services (Domain) ----
builder.Services.AddScoped<ITicketDomainService, TicketDomainService>();
builder.Services.AddScoped<ITicketDetailDomainService, TicketDetailDomainService>();
builder.Services.AddScoped<IOrderDeductionDomainService, OrderDeductionDomainService>();

// ---- Transactional outbox (Application abstraction + EF impl) ----
builder.Services.AddScoped<IOrderMqTransactionService, OrderMqTransactionServiceImpl>();

// ---- Application services (Application) ----
builder.Services.AddScoped<ITicketAppService, TicketAppServiceImpl>();
builder.Services.AddScoped<ITicketDetailAppService, TicketDetailAppServiceImpl>();
builder.Services.AddScoped<ITicketOrderAppService, TicketOrderAppServiceImpl>();
builder.Services.AddScoped<IOrderMqAppService, OrderMqAppServiceImpl>();
builder.Services.AddScoped<IOrderMqConsumerHandler, OrderMqConsumerHandlerImpl>();
builder.Services.AddScoped<IPaymentAppService, PaymentAppServiceImpl>();
builder.Services.AddScoped<IEmployeeCacheService, EmployeeCacheServiceImpl>();
builder.Services.AddScoped<IBookingAppService, BookingAppServiceImpl>();
builder.Services.AddScoped<IEventAppService, EventAppServiceImpl>();

// ---- Catalog cache (Infrastructure cache + Application abstraction) ----
builder.Services.AddScoped<ITicketCacheService, FlashSale.Infrastructure.Cache.TicketCacheService>();
builder.Services.AddScoped<FlashSale.Application.Services.ITicketDetailCacheService, FlashSale.Infrastructure.Cache.TicketDetailCacheService>();

// ---- HttpClient for circuit-breaker demo (HiController) ----
builder.Services.AddHttpClient();

// ---- Rate limiter (TASK-020) — mirrors Java Resilience4j RateLimiter ----
//   backendA: 2 req / 10 s  (was used by /hello/hi)
//   backendB: 5 req / 10 s  (was used by /hello/hi/v1)
//   queueLimit=0 in both (Java timeoutDuration=0 for backendA; 3s for backendB — we keep 0 for parity
//   with the rejection semantics of "Too many request" fallback).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("backendA", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.User.Identity?.Name ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit         = 2,
                Window              = TimeSpan.FromSeconds(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit          = 0,
                AutoReplenishment  = true,
            }));
    o.AddPolicy("backendB", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.User.Identity?.Name ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit         = 5,
                Window              = TimeSpan.FromSeconds(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit          = 0,
                AutoReplenishment  = true,
            }));
});

// ---- Polly v8 Resilience Pipeline (TASK-020) — mirrors Resilience4j @CircuitBreaker("checkRandom") ----
//   FailureRatio=0.5, MinimumThroughput=5, SamplingDuration=10s, BreakDuration=5s.
//   Matches application.yml lines 81-89 of the Java source.
builder.Services.AddResiliencePipeline(HiController.CHECK_RANDOM_PIPELINE, b =>
{
    b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio       = 0.5,
        MinimumThroughput  = 5,
        SamplingDuration   = TimeSpan.FromSeconds(10),
        BreakDuration      = TimeSpan.FromSeconds(5),
        ShouldHandle       = new PredicateBuilder().Handle<HttpRequestException>()
                                                   .Handle<TaskCanceledException>()
                                                   .Handle<TimeoutException>(),
    });
});

// ---- Outbox publisher ----
builder.Services.AddHostedService<OutboxPublisherWorker>();

// ---- Catalog warmup (TASK-011f) ----
builder.Services.AddHostedService<WarmupDataWorker>();

// ---- Controllers / OpenAPI ----
builder.Services.AddControllers().AddJsonOptions(opts =>
{
    opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseRouting();
app.UseForwardedHeaders();
app.UseRateLimiter();   // TASK-020 — wire [EnableRateLimiting] for /hello/hi and /hello/hi/v1

// Prometheus /metrics
app.UseHttpMetrics();
app.MapMetrics("/metrics");

// Health checks (basic — extended in TASK-010)
app.MapGet("/health", () => Results.Ok(new { status = "ok", at = DateTimeOffset.UtcNow }));

app.MapControllers();

try
{
    Log.Information("FlashSale starting on port 5080");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "FlashSale terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }