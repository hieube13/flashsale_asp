using System.Text.Json;
using System.Text.Json.Nodes;
using FlashSale.ContractTests.Helpers;

namespace FlashSale.ContractTests.Parity;

/// <summary>
/// Payment parity tests.
/// All tests [Fact(Skip=…)] — requires .NET app + Docker infra + seeded order data.
/// </summary>
public class PaymentParityTests
{
    private readonly ContractTestHttpClient _http = new();
    private const string SkipReason =
        "Requires .NET app (port 5080) + Docker infra. "
      + "docker compose up -d && dotnet run --project src/FlashSale.Api "
      + "&& dotnet test --filter FullyQualifiedName~PaymentParity";

    private static JsonNode Parse(string json)
        => JsonNode.Parse(json) ?? throw new InvalidOperationException($"Invalid JSON: {json}");

    private static async Task<string> PlaceCasOrder(ContractTestHttpClient http)
    {
        var payload = new { ticketId = 1L, quantity = 1 };
        var resp = await http.PostAsync("/order/cas",
            new StringContent(JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        return body["result"]!.AsObject()["orderNumber"]!.GetValue<string>();
    }

    [Fact(Skip = SkipReason)]
    public async Task CreatePayment_returns_paymentUrl()
    {
        var orderNumber = await PlaceCasOrder(_http);

        var payload = new { userId = 1L, orderNumber, method = "vnpay" };
        var resp = await _http.PostAsync("/payment/create",
            new StringContent(JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True((bool?)body["success"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task CreatePayment_nonexistentOrder_returns_failure()
    {
        var payload = new { userId = 1L, orderNumber = "NONEXISTENT", method = "vnpay" };
        var resp = await _http.PostAsync("/payment/create",
            new StringContent(JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.False((bool?)body["success"]);
    }

    [Fact(Skip = SkipReason)]
    public async Task PaymentCallbackReturn_returns_html()
    {
        var resp = await _http.GetAsync("/payment/callback/return");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("html", body.ToLowerInvariant());
    }

    [Fact(Skip = SkipReason)]
    public async Task PaymentCallbackIpn_returns_raw_rspCode()
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vnp_ResponseCode"] = "00",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_TxnRef"] = Guid.NewGuid().ToString("N"),
            ["vnp_Amount"] = "100000",
        });

        var resp = await _http.PostAsync("/payment/callback/ipn", form);
        resp.EnsureSuccessStatusCode();

        var body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.NotNull(body["RspCode"]);
        Assert.NotNull(body["Message"]);
        Assert.Null(body["success"]); // raw {RspCode, Message} — not ResultMessage
    }
}
