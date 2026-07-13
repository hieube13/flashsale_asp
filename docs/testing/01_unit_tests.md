# Unit test conventions

## Framework

- **xUnit** as the test runner
- **FluentAssertions** for readable assertions (`.Should().Be(...)`)
- **Moq** or **NSubstitute** for mocking

## Naming

```
MethodName_StateUnderTest_ExpectedBehavior
```

Examples:
- `PlaceOrderCasAsync_CacheMissAndDbHasStock_ReturnsSuccess`
- `DecreaseStockAsync_StockBelowZero_ReturnsFalse`
- `ExtractYearMonth_ValidOrderNumber_ReturnsSixDigits`

## Structure

```csharp
public class TicketOrderAppServiceTests
{
    private readonly Mock<ITicketRepository> _ticketRepo = new();
    private readonly Mock<IStockOrderCacheService> _cache = new();
    private readonly Mock<IDbConnectionFactory> _conn = new();
    private readonly TicketOrderAppServiceImpl _sut;

    public TicketOrderAppServiceTests()
    {
        _sut = new TicketOrderAppServiceImpl(_ticketRepo.Object, _cache.Object, _conn.Object);
    }

    [Fact]
    public async Task PlaceOrderCasAsync_CacheHit_UpdatesDbAndReturnsOk()
    {
        // Arrange
        _cache.Setup(c => c.DecreaseStockCacheByLuaAsync(4, 2, It.IsAny<CancellationToken>()))
              .ReturnsAsync(1);
        _ticketRepo.Setup(r => r.GetPriceFlashAsync(4, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(10000L);
        // ... db mock for UPDATE returning 1 row
        
        // Act
        var result = await _sut.PlaceOrderCasAsync(4, 2);
        
        // Assert
        result.Success.Should().BeTrue();
        result.OrderNumber.Should().StartWith("OKX-SGN-");
    }
}
```

## What to test

- ✅ Happy path
- ✅ Each return-code branch of Lua (-1, 0, 1)
- ✅ DB failure with Redis compensation
- ✅ Price lookup failure handling
- ✅ Order number format (regex)
- ✅ YearMonth extraction edge cases (wrong format, missing segments)
- ✅ Lock acquisition timeout
- ❌ Don't test the framework (e.g., that EF Core saves changes)
- ❌ Don't test logging (use a logging test pattern, separate concern)

## Mock vs stub

- **Mock**: behaviour-verification (e.g., "was `DecreaseStockAsync` called with quantity=2?")
- **Stub**: state-setup (return value)
- Prefer stub for query-side, mock for command-side

## Don't

- Don't call actual DB / Redis / Kafka in unit tests
- Don't load `Program.cs` (that's for IntegrationTests)
- Don't use `Thread.Sleep` — use `Task.Delay` with cancellation
- Don't share state between tests (use fresh `IClassFixture` per class, never `IAssemblyFixture`)

## Coverage

- Aim 80% line coverage for service implementations
- 100% for domain entities (small, pure)
- Architecture tests catch dependency violations

## Run

```powershell
dotnet test tests/FlashSale.UnitTests
# Test summary: total: X, failed: 0, succeeded: X, skipped: 0
```