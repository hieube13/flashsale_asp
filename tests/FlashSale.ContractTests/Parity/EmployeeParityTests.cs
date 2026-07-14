using System.Text.Json;
using System.Text.Json.Nodes;
using FlashSale.ContractTests.Helpers;

namespace FlashSale.ContractTests.Parity;

/// <summary>
/// Employee timesheet parity tests.
/// NOTE: Employee endpoints return ResultMessage&lt;T&gt; in .NET,
/// while Java returns raw types. Documented in KNOWN_DIFFERENCES.md §32.
///
/// All tests [Fact(Skip=…)] — requires .NET app + Docker infra (Redis).
/// </summary>
public class EmployeeParityTests
{
    private readonly ContractTestHttpClient _http = new();
    private const long TestUserId = 9999;
    private const string SkipReason =
        "Requires .NET app (port 5080) + Docker infra (Redis). "
      + "docker compose up -d && dotnet run --project src/FlashSale.Api "
      + "&& dotnet test --filter FullyQualifiedName~EmployeeParity";

    private static JsonNode Parse(string json)
        => JsonNode.Parse(json) ?? throw new InvalidOperationException($"Invalid JSON: {json}");

    [Fact(Skip = SkipReason)]
    public async Task SignIn_returns_resultMessage()
    {
        var resp = await _http.PostAsync($"/api/sign-in/{TestUserId}", null);
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
        Assert.Equal(200, (int?)body["code"]);
        // .NET wraps in ResultMessage; Java raw — KNOWN_DIFFERENCES §32
        Assert.NotNull(body["result"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task SignInAnyDate_returns_resultMessage()
    {
        var resp = await _http.PostAsync($"/api/sign-in/{TestUserId}/any-date?date=2026-07-01", null);
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task CheckSignIn_returns_resultMessage_with_bool()
    {
        var resp = await _http.GetAsync($"/api/sign-in/{TestUserId}/check?date=2026-07-01");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
        // .NET wraps bool in ResultMessage; Java raw bool — KNOWN_DIFFERENCES §32
        Assert.NotNull(body["result"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task MonthlyCount_returns_resultMessage_with_long()
    {
        var resp = await _http.GetAsync($"/api/sign-in/{TestUserId}/monthly-count?month=2026-07");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
        Assert.NotNull(body["result"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task MonthlySignDetails_returns_resultMessage()
    {
        var resp = await _http.GetAsync($"/api/sign-in/{TestUserId}/monthly-sign-details?month=2026-07");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
        var result = body["result"]!.AsObject();
        Assert.NotNull(result["totalSignCount"]);
        Assert.NotNull(result["signDays"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task FirstDay_returns_resultMessage_with_int()
    {
        var resp = await _http.GetAsync($"/api/sign-in/{TestUserId}/first-day?month=2026-07");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
        Assert.NotNull(body["result"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task ConsecutiveDays_returns_resultMessage()
    {
        var resp = await _http.GetAsync($"/api/sign-in/{TestUserId}/consecutive-days?date=2026-07-10");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
        Assert.NotNull(body["result"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task Summary_returns_resultMessage_with_required_fields()
    {
        var resp = await _http.GetAsync($"/api/sign-in/{TestUserId}/summary?date=2026-07-10");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
        var result = body["result"]!.AsObject();
        Assert.NotNull(result["date"]);
        Assert.NotNull(result["hasSignedIn"]);
        Assert.NotNull(result["monthlyCount"]);
        Assert.NotNull(result["firstSignDay"]);
        Assert.NotNull(result["consecutiveDays"]);
    }
}
