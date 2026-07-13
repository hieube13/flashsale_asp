# Integration test conventions

## Framework

- **xUnit** + **Microsoft.AspNetCore.Mvc.Testing** (`WebApplicationFactory<Program>`)
- **Testcontainers** for MySQL, Redis, Kafka
- One container per test class (via `IClassFixture<TestcontainersFixture>`)

## Test fixture

```csharp
public class TestcontainersFixture : IAsyncLifetime
{
    public MySqlContainer MySql { get; }
    public RedisContainer Redis { get; }
    public KafkaContainer Kafka { get; }
    
    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            MySql.StartAsync(),
            Redis.StartAsync(),
            Kafka.StartAsync());
        
        // Apply schema
        using var conn = new MySqlConnection(MySql.GetConnectionString());
        var sql = await File.ReadAllTextAsync("environment/mysql/init/ticket_init.sql");
        // naive split on ; — improve in real fixture
        foreach (var statement in sql.Split(";", StringSplitOptions.RemoveEmptyEntries))
            await conn.ExecuteAsync(statement);
    }
    
    public async Task DisposeAsync()
    {
        await MySql.DisposeAsync();
        await Redis.DisposeAsync();
        await Kafka.DisposeAsync();
    }
}
```

## WebApplicationFactory

```csharp
public class FlashSaleFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(o =>
        {
            o.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MySql"] = _fixture.MySql.GetConnectionString(),
                ["Redis:ConnectionString"] = _fixture.Redis.GetConnectionString(),
                ["Kafka:BootstrapServers"] = _fixture.Kafka.GetBootstrapAddress()
            });
        });
        return base.CreateHost(builder);
    }
}
```

## Test example

```csharp
public class OrderApiTests : IClassFixture<FlashSaleFactory>
{
    private readonly HttpClient _client;
    
    public OrderApiTests(FlashSaleFactory factory)
    {
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task PostOrderCas_ReturnsOkAndDecrementsStock()
    {
        // Arrange
        await SeedTicket(4, stockAvailable: 100);
        
        // Act
        var response = await _client.PostAsJsonAsync("/order/cas", new { ticketId = 4, quantity = 2 });
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PlaceOrderResponse>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        
        var ticketStock = await GetStockAvailable(4);
        ticketStock.Should().Be(98);
    }
    
    [Fact]
    public async Task PostOrderCas_WhenOOS_ReturnsFailureNoDecrement()
    {
        await SeedTicket(4, stockAvailable: 1);
        
        var response = await _client.PostAsJsonAsync("/order/cas", new { ticketId = 4, quantity = 10 });
        
        var body = await response.Content.ReadFromJsonAsync<PlaceOrderResponse>();
        body!.Success.Should().BeFalse();
        body.Code.Should().Be("OUT_OF_STOCK");
        
        var ticketStock = await GetStockAvailable(4);
        ticketStock.Should().Be(1); // unchanged
    }
}
```

## Cleanup

- `Respawn` library to truncate tables between tests
- Flush Redis between tests
- Recreate Kafka topics (or use unique topic per test)

## Performance

- Container startup ~10-30s
- Test method < 5s
- Avoid `WebApplicationFactory.CreateClient()` inside test (use constructor)

## What to test

- ✅ Full HTTP → service → DB → Redis → Kafka roundtrip
- ✅ Concurrent operations on same resource (CAS test: 1000 concurrent requests, stock never negative)
- ✅ Transaction rollback paths
- ✅ Outbox publisher picks up events under concurrent workers
- ❌ Don't test pure logic (covered by unit tests)
- ❌ Don't test every endpoint — pick the critical ones

## Run

```powershell
dotnet test tests/FlashSale.IntegrationTests
# Requires Docker. Tests are slower (~1-2 min total)
```