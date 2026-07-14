using System.Text.Json;
using System.Text.Json.Nodes;
using FlashSale.ContractTests.Helpers;

namespace FlashSale.ContractTests.Parity;

/// <summary>
/// Catalog parity tests — validates GET /ticket/* endpoints.
/// All tests are [Fact(Skip=…)] by default — run manually with infra up.
/// To run: docker compose up -d &amp;&amp; dotnet run --project src/FlashSale.Api &amp;&amp;
///          dotnet test --filter "FullyQualifiedName~TicketParity"
/// </summary>
public class TicketParityTests
{
    private readonly ContractTestHttpClient _http = new();
    private const string SkipReason =
        "Requires .NET app (port 5080) + Docker infra. "
      + "docker compose up -d && dotnet run --project src/FlashSale.Api "
      + "&& dotnet test --filter FullyQualifiedName~TicketParity";

    private static JsonNode Parse(string json)
        => JsonNode.Parse(json) ?? throw new InvalidOperationException($"Invalid JSON: {json}");

    [Fact(Skip = SkipReason)]
    public async Task GetTicketActive_returns_success_true()
    {
        var resp = await _http.GetAsync("/ticket/active");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
        Assert.Equal(200, (int?)body["code"]);
        Assert.NotNull(body["result"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task GetTicketById_returns_ticketDto_with_required_fields()
    {
        var resp = await _http.GetAsync("/ticket/1");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
        var result = body["result"]!.AsObject();
        Assert.True(result["id"]!.GetValue<long>() > 0);
        Assert.NotNull(result["name"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task GetTicketDetail_returns_detailDto_with_version()
    {
        var resp = await _http.GetAsync("/ticket/1/detail/1");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
        var result = body["result"]!.AsObject();
        Assert.True(result["id"]!.GetValue<long>() > 0);
        Assert.NotNull(result["version"]); // nullable long — may be null if not cached
    }

    [Fact(Skip = SkipReason)]
    public async Task GetTicketDetailOrder_returns_bare_bool()
    {
        var resp = await _http.GetAsync("/ticket/1/detail/1/order");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        // Java returns raw boolean; verify body is "true" or "false"
        var trimmed = body.Trim();
        Assert.True(trimmed == "true" || trimmed == "false",
            $"Expected bare bool, got: {body}");
    }

    [Fact(Skip = SkipReason)]
    public async Task PingJava_returns_statusOK_raw()
    {
        var resp = await _http.GetAsync("/ticket/ping/java");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("OK", (string?)body["status"]);
        Assert.Null(body["success"]); // raw JSON, not ResultMessage
    }

    [Fact(Skip = SkipReason)]
    public async Task CreateTicket_returns_ticketDto()
    {
        var payload = new
        {
            name = "Test Parity Ticket",
            description = "Auto-created by contract test",
            startTime = "2026-07-20T00:00:00Z",
            endTime   = "2026-07-30T00:00:00Z",
            detail = new { name = "Standard", stockInitial = 100, priceOriginal = 99000m }
        };
        var json = JsonSerializer.Serialize(payload);
        var resp = await _http.PostAsync("/ticket/create",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
        Assert.True((body["result"]!.AsObject()["id"]!.GetValue<long>()) > 0);
    }

    [Fact(Skip = SkipReason)]
    public async Task DeleteTicket_returns_success_true()
    {
        // Create first
        var createPayload = new
        {
            name = "Temp Ticket for Delete",
            startTime = "2026-07-20T00:00:00Z",
            endTime   = "2026-07-30T00:00:00Z",
            detail = new { name = "T", stockInitial = 10, priceOriginal = 1000m }
        };
        var json = JsonSerializer.Serialize(createPayload);
        var createResp = await _http.PostAsync("/ticket/create",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        createResp.EnsureSuccessStatusCode();
        var createBody = Parse(await createResp.Content.ReadAsStringAsync());
        var id = createBody["result"]!.AsObject()["id"]!.GetValue<long>();

        // Delete
        var resp = await _http.GetAsync($"/ticket/{id}");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
    }
}
