using FlashSale.Api.Stubs;
using FlashSale.Api.Workers;
using FlashSale.Application.Services;
using FlashSale.Contracts.Messages;
using FlashSale.Infrastructure.Cache;
using FlashSale.Infrastructure.Data;
using FlashSale.Infrastructure.DistributedLock;
using FlashSale.Infrastructure.External;
using FlashSale.Infrastructure.Messaging;
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

// ---- Application services (placeholders for now — concrete impls added in TASK-008/011..017) ----
builder.Services.AddScoped<ITicketAppService, TicketAppServiceStub>();
builder.Services.AddScoped<ITicketDetailAppService, TicketDetailAppServiceStub>();
builder.Services.AddScoped<ITicketOrderAppService, TicketOrderAppServiceStub>();
builder.Services.AddScoped<IOrderMqAppService, OrderMqAppServiceStub>();
builder.Services.AddScoped<IOrderMqConsumerHandler, OrderMqConsumerHandlerStub>();
builder.Services.AddScoped<IPaymentAppService, PaymentAppServiceStub>();
builder.Services.AddScoped<IBookingAppService, BookingAppServiceStub>();
builder.Services.AddScoped<IEmployeeCacheService, EmployeeCacheServiceStub>();
builder.Services.AddScoped<IEventAppService, EventAppServiceStub>();

// ---- Outbox publisher ----
builder.Services.AddHostedService<OutboxPublisherWorker>();

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