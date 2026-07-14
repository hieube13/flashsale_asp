using System.Text.Json;
using System.Text.Json.Nodes;
using FlashSale.ContractTests.Helpers;

namespace FlashSale.ContractTests.Parity;

/// <summary>
/// Order + OrderMQ parity tests — validates /order/* endpoints.
/// All tests [Fact(Skip=…)] — requires .NET app + Docker infra.
/// Run: docker compose up -d &amp;&amp; dotnet run --project src/FlashSale.Api
///      &amp;&amp; dotnet test --filter "FullyQualifiedName~OrderParity"
/// </summary>
public class OrderParityTests
{
    private readonly ContractTestHttpClient _http = new();
    private const string SkipReason =
        "Requires .NET app (port 5080) + Docker infra. "
      + "docker compose up -d && dotnet run --project src/FlashSale.Api "
      + "&& dotnet test --filter FullyQualifiedName~OrderParity";

    private static JsonNode Parse(string json)
        => JsonNode.Parse(json) ?? throw new InvalidOperationException($"Invalid JSON: {json}");

    private static async Task<string> PlaceCasOrder(ContractTestHttpClient http)
    {
        var payload = new { ticketId = 1L, quantity = 1 };
        var json = JsonSerializer.Serialize(payload);
        var resp = await http.PostAsync("/order/cas",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        return body["result"]!.AsObject()["orderNumber"]!.GetValue<string>();
    }

    [Fact(Skip = SkipReason)]
    public async Task PlaceOrderCas_returns_success_true()
    {
        var orderNumber = await PlaceCasOrder(_http);
        Assert.False(string.IsNullOrEmpty(orderNumber));
    }

    [Fact(Skip = SkipReason)]
    public async Task PlaceOrderCas_outOfStock_returns_failure()
    {
        var payload = new { ticketId = 9999L, quantity = 1 };
        var resp = await _http.PostAsync("/order/cas",
            new StringContent(JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.False((bool?)body["success"]);
        Assert.Equal("OUT_OF_STOCK", (string?)body["code"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task DecreaseStockOrder_returns_body()
    {
        var resp = await _http.GetAsync("/order/1/1/order");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body));
    }

    [Fact(Skip = SkipReason)]
    public async Task DecreaseStockCas_returns_body()
    {
        var resp = await _http.GetAsync("/order/1/1/cas");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body));
    }

    [Fact(Skip = SkipReason)]
    public async Task DecreaseStockQueue_returns_bare_bool()
    {
        var resp = await _http.GetAsync("/order/1/1/1/queued");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        var trimmed = body.Trim();
        Assert.True(trimmed == "true" || trimmed == "false",
            $"Expected bare bool, got: {body}");
    }

    [Fact(Skip = SkipReason)]
    public async Task FindAllOrders_returns_success_true()
    {
        var ym = DateTime.UtcNow.ToString("yyyyMM");
        var resp = await _http.GetAsync($"/order/1/list?yearMonth={ym}");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task FindPageOrders_returns_pagedOrdersDto()
    {
        var ym = DateTime.UtcNow.ToString("yyyyMM");
        var resp = await _http.GetAsync($"/order/1/list/page?yearMonth={ym}&cursor=0&limit=10");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
        var result = body["result"]!.AsObject();
        Assert.NotNull(result["items"]);
        Assert.NotNull(result["hasMore"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task CancelOrder_returns_success_true()
    {
        var orderNumber = await PlaceCasOrder(_http);
        var resp = await _http.PutAsync($"/order/1/{orderNumber}", null);
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task PlaceOrderMq_returns_success_true()
    {
        var payload = new { ticketId = 1L, quantity = 1 };
        var resp = await _http.PostAsync("/order/mq",
            new StringContent(JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task GetOrderMqStatus_returns_orderQueue()
    {
        var payload = new { ticketId = 1L, quantity = 1 };
        var placeResp = await _http.PostAsync("/order/mq",
            new StringContent(JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8, "application/json"));
        placeResp.EnsureSuccessStatusCode();
        var placeBody = Parse(await placeResp.Content.ReadAsStringAsync());
        var token = placeBody["result"]!.AsObject()["orderNumber"]?.GetValue<string>();
        if (string.IsNullOrEmpty(token)) return; // infra not ready

        var resp = await _http.GetAsync($"/order/mq/status/{token}");
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
    }
}
