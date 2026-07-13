using FlashSale.Api.Stubs;
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
using FlashSale.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Serilog ----
builder.Host.UseSerilog((ctx, lc) =>
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext()
      .WriteTo.Console());

// ---- Kestrel / port 5080 (Java dùng 1122) ----
builder.WebHost.ConfigureKestrel(opts =>
{
    opts.ListenAnyIP(5080);
});

// ---- DB ----
var mysqlConn = builder.Configuration.GetConnectionString("MySql")
    ?? throw new InvalidOperationException("ConnectionStrings:MySql missing");
builder.Services.AddDbContext<FlashSaleDbContext>(opts =>
    opts.UseMySql(mysqlConn, ServerVersion.AutoDetect(mysqlConn)));
builder.Services.Configure<MySqlOptions>(o => o.ConnectionString = mysqlConn);
builder.Services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();

// ---- Redis ----
var redisConn = builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis:ConnectionString missing");
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
    StackExchange.Redis.ConnectionMultiplexer.Connect(redisConn));
builder.Services.AddSingleton<IRedisInfrasService, RedisInfrasService>();
builder.Services.AddSingleton<IStockOrderCacheService, StockOrderCacheService>();

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

// ---- Domain services (Domain) ----
builder.Services.AddScoped<ITicketDomainService, TicketDomainService>();
builder.Services.AddScoped<ITicketDetailDomainService, TicketDetailDomainService>();
builder.Services.AddScoped<IOrderDeductionDomainService, OrderDeductionDomainService>();

// ---- Application services (Application) ----
builder.Services.AddScoped<ITicketAppService, TicketAppServiceImpl>();
builder.Services.AddScoped<ITicketDetailAppService, TicketDetailAppServiceImpl>();
builder.Services.AddScoped<ITicketOrderAppService, TicketOrderAppServiceImpl>();

// ---- Catalog cache (Infrastructure cache + Application abstraction) ----
builder.Services.AddScoped<ITicketCacheService, FlashSale.Infrastructure.Cache.TicketCacheService>();
builder.Services.AddScoped<FlashSale.Application.Services.ITicketDetailCacheService, FlashSale.Infrastructure.Cache.TicketDetailCacheService>();

// ---- Other application services — stubs until later tasks land their real impls ----
builder.Services.AddScoped<IOrderMqAppService, OrderMqAppServiceStub>();
builder.Services.AddScoped<IOrderMqConsumerHandler, OrderMqConsumerHandlerStub>();
builder.Services.AddScoped<IPaymentAppService, PaymentAppServiceStub>();
builder.Services.AddScoped<IBookingAppService, BookingAppServiceStub>();
builder.Services.AddScoped<IEmployeeCacheService, EmployeeCacheServiceStub>();
builder.Services.AddScoped<IEventAppService, EventAppServiceStub>();

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