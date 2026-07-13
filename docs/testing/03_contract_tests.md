# Contract tests — Java vs .NET parity

## Goal

Run every .NET endpoint and verify the JSON body matches the Java baseline byte-for-byte (allowing only `timestamp` differences).

## Capture baseline

One-off process, repeated whenever Java endpoints change:

```powershell
# 1. Run Java locally on port 1122 (mvn spring-boot:run)
# 2. Run .NET locally on port 5080 (dotnet run --project src/FlashSale.Api)
# 3. Run capture script
.\tests\FlashSale.ContractTests\Scripts\capture-baselines.ps1 `
  -JavaBase "http://localhost:1122" `
  -DotnetBase "http://localhost:5080" `
  -OutputDir "tests/FlashSale.ContractTests/Baselines"

# Script will:
# - Curl each endpoint from BOTH servers
# - Diff bodies (excluding timestamp)
# - If match, save to Baselines/{route}.json
# - If differ, log diff and FAIL
```

## Test execution

```csharp
public class TicketContractTests : IClassFixture<FlashSaleFactory>
{
    [Theory]
    [InlineData("GET", "/ticket/active")]
    [InlineData("GET", "/ticket/4")]
    // ... all routes
    public async Task Endpoint_MatchesJavaBaseline(string method, string path)
    {
        var baseline = await File.ReadAllTextAsync($"Baselines/{method}_{path}.json");
        var expected = JsonNode.Parse(baseline)!;
        
        var response = await _client.GetAsync(path);
        var actual = await response.Content.ReadFromJsonAsync<JsonNode>();
        
        JsonDiff.AssertEqual(expected, actual, exclude: new[] { "timestamp" });
    }
}
```

## JSON diff rules

- ✅ Allow `timestamp` field difference (server time)
- ❌ Any other difference = test fails
- Array order matters (matches Java order)
- Numeric precision matters — Java uses `BigDecimal` often, .NET uses `decimal` — but JSON renders both identically

## Diff helper

```csharp
public static class JsonDiff
{
    public static void AssertEqual(JsonNode expected, JsonNode actual, IEnumerable<string> exclude)
    {
        var exp = Normalize(expected, exclude);
        var act = Normalize(actual, exclude);
        var expJson = exp.ToJsonString();
        var actJson = act.ToJsonString();
        if (expJson != actJson)
        {
            throw new XunitException(
                $"JSON mismatch.\n--- Expected ---\n{expJson}\n--- Actual ---\n{actJson}\n--- Diff ---\n{Diff(expJson, actJson)}");
        }
    }
    
    private static JsonNode Normalize(JsonNode node, IEnumerable<string> exclude)
    {
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var kv in obj)
                if (!exclude.Contains(kv.Key))
                    result[kv.Key] = Normalize(kv.Value!, exclude);
            return result;
        }
        // arrays + values pass through
        return node;
    }
}
```

## What's in scope

All endpoints listed in [docs/EXECUTE.md](../EXECUTE.md) §HTTP routes that must stay identical — 22 routes.

## What's NOT in scope

- Performance (covered by load tests)
- Logging format (internal)
- Header values (Date, Server, etc.)

## When to refresh baselines

- Java codebase changes (rare — read-only during migration)
- New endpoint added in .NET (must have Java equivalent)
- Known differences added (must match — see KNOWN_DIFFERENCES.md)

## Run

```powershell
dotnet test tests/FlashSale.ContractTests
```

## Caveats

- Baseline capture requires Java running
- Baselines are JSON files committed to git
- Tests must be deterministic — no random IDs in responses (Java uses random but expect same range; document random in KNOWN_DIFFERENCES)