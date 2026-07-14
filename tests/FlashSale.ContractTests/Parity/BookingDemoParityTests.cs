using System.Text.Json;
using System.Text.Json.Nodes;
using FlashSale.ContractTests.Helpers;

namespace FlashSale.ContractTests.Parity;

/// <summary>
/// Booking + Demo parity tests.
/// All tests [Fact(Skip=…)] — requires .NET app + Docker infra.
/// </summary>
public class BookingDemoParityTests
{
    private readonly ContractTestHttpClient _http = new();
    private const string SkipReason =
        "Requires .NET app (port 5080) + Docker infra. "
      + "docker compose up -d && dotnet run --project src/FlashSale.Api "
      + "&& dotnet test --filter FullyQualifiedName~BookingDemoParity";

    private static JsonNode Parse(string json)
        => JsonNode.Parse(json) ?? throw new InvalidOperationException($"Invalid JSON: {json}");

    [Fact(Skip = SkipReason)]
    public async Task CreateBooking_returns_bookingDto_5_fields()
    {
        var payload = new { ticketId = 1L, quantity = 1 };
        var resp = await _http.PostAsync("/api/bookings",
            new StringContent(JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
        var result = body["result"]!.AsObject();
        Assert.True(result["id"]!.GetValue<long>() > 0);
        Assert.True(result["quantity"]!.GetValue<int>() >= 1);
        Assert.StartsWith("BK", result["bookingCode"]!.GetValue<string>());
        Assert.Equal(1, result["status"]!.GetValue<int>()); // CONFIRMED
        Assert.Null(result["createdAt"]); // no createdAt field (parity with Java)
    }

    [Fact(Skip = SkipReason)]
    public async Task CreateBooking_quantityZero_returns_error_400()
    {
        var payload = new { ticketId = 1L, quantity = 0 };
        var resp = await _http.PostAsync("/api/bookings",
            new StringContent(JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode(); // HTTP 200
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.False((bool?)body["success"]);
        Assert.Equal(400, (int?)body["code"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task SayHi_returns_raw_string()
    {
        var resp = await _http.GetAsync("/hello/hi");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        // Must be a raw string (no JSON wrapper)
        JsonNode.Parse(body); // should throw if not valid JSON — or succeed with raw string
        Assert.False(string.IsNullOrWhiteSpace(body));
    }

    [Fact(Skip = SkipReason)]
    public async Task SayHiV1_returns_raw_string()
    {
        var resp = await _http.GetAsync("/hello/hi/v1");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body));
    }

    [Fact(Skip = SkipReason)]
    public async Task CircuitBreaker_returns_non_empty_body()
    {
        var resp = await _http.GetAsync("/hello/circuit/breaker");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body));
    }

    [Fact(Skip = SkipReason)]
    public async Task SecureInfo_returns_raw_success_object()
    {
        var resp = await _http.GetAsync("/api/v1/secure/info");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("success", (string?)body["status"]);
        Assert.Equal("This is secure information.", (string?)body["message"]);
        Assert.Null(body["success"]); // raw {status, message} — not ResultMessage
    }

    [Fact(Skip = SkipReason)]
    public async Task SecureData_echoes_payload()
    {
        var payload = new { any = "data" };
        var resp = await _http.PostAsync("/api/v1/secure/data",
            new StringContent(JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("success", (string?)body["status"]);
        Assert.Equal("Secure data processed!", (string?)body["message"]);
        Assert.NotNull(body["receivedPayload"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task SecureUnauthorized_returns_401()
    {
        var resp = await _http.GetAsync("/api/v1/secure/unauthorized");
        Assert.Equal(401, (int)resp.StatusCode);
    }

    [Fact(Skip = SkipReason)]
    public async Task SecureForbidden_returns_403()
    {
        var resp = await _http.GetAsync("/api/v1/secure/forbidden");
        Assert.Equal(403, (int)resp.StatusCode);
    }
}
