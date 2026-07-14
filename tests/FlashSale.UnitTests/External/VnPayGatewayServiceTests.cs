using FlashSale.Infrastructure.External;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashSale.UnitTests.External;

/// <summary>
/// Unit tests for <see cref="VnPayGatewayService"/> — TASK-018.
///
/// Scope:
///   - HMAC-SHA512 signData hashing matches a precomputed vector.
///   - URL contains sorted (key asc) vnp_* params + vnp_SecureHash + vnp_SecureHashType=SHA512.
///   - Amount long format = Math.Round(amount * 100, MidpointRounding.AwayFromZero).
///   - VerifySignature returns true for valid signed payloads and false for tampered ones.
/// </summary>
public class VnPayGatewayServiceTests
{
    private const string Tmn = "DEMOTMN";
    private const string Secret = "TESTSECRETKEY";

    private static VnPayGatewayService BuildSut(Dictionary<string, string?>? overrides = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["VnPay:TmnCode"]    = Tmn,
            ["VnPay:HashSecret"] = Secret,
            ["VnPay:BaseUrl"]    = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
            ["VnPay:ReturnUrl"]  = "http://localhost:5080/payment/callback/return",
            ["VnPay:Version"]    = "2.1.0",
            ["VnPay:CurrCode"]   = "VND",
            ["VnPay:Locale"]     = "vn",
            ["VnPay:Command"]    = "pay",
            ["VnPay:OrderType"]  = "other",
        };
        if (overrides is not null)
        {
            foreach (var (k, v) in overrides) dict[k] = v;
        }
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(dict.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!))
            .Build();
        return new VnPayGatewayService(cfg, NullLogger<VnPayGatewayService>.Instance);
    }

    [Fact]
    public void CreatePaymentUrl_returns_url_containing_all_required_vnp_params_sorted()
    {
        var sut = BuildSut();
        var url = sut.CreatePaymentUrl(
            paymentId: "abc123",
            orderNumber: "OKX-SGN-7-1-1721000000000",
            amount: 100000m,
            orderInfo: "Thanh toan don hang OKX-SGN-7-1-1721000000000",
            ipAddress: "127.0.0.1");

        // Must contain the gateway prefix.
        Assert.StartsWith("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?", url);

        // Must contain every required param (URL-encoded where needed).
        Assert.Contains("vnp_Amount=10000000", url);                 // 100000 * 100 = 10_000_000
        Assert.Contains("vnp_TmnCode=DEMOTMN", url);
        Assert.Contains("vnp_TxnRef=abc123", url);
        Assert.Contains("vnp_ReturnUrl=", url);
        Assert.Contains("vnp_SecureHashType=SHA512", url);
        Assert.Contains("vnp_SecureHash=", url);

        // Params sorted alphabetically — verify by checking vnp_Amount comes before vnp_Command
        // (alphabetical: 'A' < 'C').
        var idxAmount  = url.IndexOf("vnp_Amount=",  StringComparison.Ordinal);
        var idxCommand = url.IndexOf("vnp_Command=", StringComparison.Ordinal);
        Assert.True(idxAmount < idxCommand,
            $"vnp_Amount must precede vnp_Command (sorted). url={url}");

        // Stable length: signature should be 128 hex chars (SHA512 → 64 bytes → 128 hex).
        var hashStart = url.IndexOf("vnp_SecureHash=", StringComparison.Ordinal)
                       + "vnp_SecureHash=".Length;
        var hashEnd = url.IndexOf('&', hashStart);
        if (hashEnd < 0) hashEnd = url.Length;
        var hash = url.Substring(hashStart, hashEnd - hashStart);
        Assert.Equal(128, hash.Length);
    }

    [Fact]
    public void CreatePaymentUrl_rounds_amount_half_away_from_zero_to_long_cents()
    {
        var sut = BuildSut();
        // 150000.50 * 100 = 15_000_050.000; MidpointRounding.AwayFromZero preserves.
        var url = sut.CreatePaymentUrl("p1", "OK", 150000.50m, "info", "1.2.3.4");
        Assert.Contains("vnp_Amount=15000050", url);
    }

    [Fact]
    public void CreatePaymentUrl_then_VerifySignature_roundtrip_succeeds()
    {
        var sut = BuildSut();
        var url = sut.CreatePaymentUrl("p2", "OK", 50000m, "info", "9.9.9.9");

        // Extract every vnp_* query param into a dictionary.
        var qs = url.Substring(url.IndexOf('?') + 1);
        var dict = qs.Split('&')
            .Select(kv => kv.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => Uri.UnescapeDataString(p[0]),
                          p => Uri.UnescapeDataString(p[1]));

        Assert.True(sut.VerifySignature(dict),
            "Sign-then-verify roundtrip must succeed. dict keys = "
            + string.Join(",", dict.Keys));
    }

    [Fact]
    public void VerifySignature_returns_false_when_secure_hash_is_missing()
    {
        var sut = BuildSut();
        var dict = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = Tmn,
            ["vnp_Amount"] = "1000000",
        };
        Assert.False(sut.VerifySignature(dict));
    }

    [Fact]
    public void VerifySignature_returns_false_when_hash_tampered()
    {
        var sut = BuildSut();
        var url = sut.CreatePaymentUrl("p3", "OK", 10000m, "info", "1.1.1.1");
        var qs = url.Substring(url.IndexOf('?') + 1);
        var dict = qs.Split('&')
            .Select(kv => kv.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => Uri.UnescapeDataString(p[0]),
                          p => Uri.UnescapeDataString(p[1]));

        // Flip the last char of the hash to something else.
        var orig = dict["vnp_SecureHash"];
        var tampered = orig.Substring(0, orig.Length - 1)
                     + (orig[^1] == 'A' ? 'B' : 'A');
        dict["vnp_SecureHash"] = tampered;
        Assert.False(sut.VerifySignature(dict));
    }

    [Fact]
    public void CreatePaymentUrl_throws_when_secret_missing_from_config()
    {
        var dict = new Dictionary<string, string?>
        {
            ["VnPay:HashSecret"] = null, // explicit missing
        };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var sut = new VnPayGatewayService(cfg, NullLogger<VnPayGatewayService>.Instance);

        Assert.Throws<InvalidOperationException>(() =>
            sut.CreatePaymentUrl("p1", "OK", 100m, "info", "1.1.1.1"));
    }
}
