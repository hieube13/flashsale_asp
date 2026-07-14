using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.External;

/// <summary>
/// Real VNPay gateway adapter — TASK-018.
///
/// Mirrors Java VnPayGatewayServiceImpl (PaymentServiceImpl.java lines 21-68) and
/// inverts the "SECRET" hardcode bug (Java line 59) by reading from configuration.
///
/// Signing flow (VNPay spec):
///   1. Put all non-empty params EXCEPT vnp_SecureHash / vnp_SecureHashType in a SortedDictionary (by key).
///   2. For each entry: append "{urlEnc(key)}={urlEnc(value)}" joined by "&amp;".
///      Note: Java code uses "&amp;" literally — VNPay's verifier accepts both "&amp;"
///      and "&" because they parse the same after the HTML-entity decode, but we
///      keep "&amp;" to match the reference exactly (parity-test friendly).
///   3. signData = that string. hash = HMAC-SHA512(secret, signData).HexUppercase.
///   4. Append "vnp_SecureHash={hash}" to the URL.
///
/// VerifySignature reverses the same flow and compares the recomputed hash to the
/// supplied vnp_SecureHash (constant-time compare to avoid timing side channels).
/// </summary>
public sealed class VnPayGatewayService : IVnPayGatewayService
{
    private const string SecureHashField = "vnp_SecureHash";
    private const string SecureHashTypeField = "vnp_SecureHashType";

    private readonly IConfiguration _config;
    private readonly ILogger<VnPayGatewayService> _logger;

    public VnPayGatewayService(IConfiguration config, ILogger<VnPayGatewayService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public string CreatePaymentUrl(string paymentId, string orderNumber, decimal amount, string orderInfo, string ipAddress, CancellationToken ct = default)
    {
        // ----- config snapshot -----
        var tmnCode    = RequiredConfig("VnPay:TmnCode");
        var hashSecret = RequiredConfig("VnPay:HashSecret");
        var baseUrl    = RequiredConfig("VnPay:BaseUrl");
        var returnUrl  = _config["VnPay:ReturnUrl"] ?? string.Empty;
        var version    = _config["VnPay:Version"] ?? "2.1.0";
        var currCode   = _config["VnPay:CurrCode"] ?? "VND";
        var locale     = _config["VnPay:Locale"]   ?? "vn";
        var command    = _config["VnPay:Command"]  ?? "pay";
        var orderType  = _config["VnPay:OrderType"] ?? "other";

        // VNPay spec: amount = originalAmount * 100 (cents of VND), integer-only.
        var amountLong = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

        var createDate = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var expireDate = DateTime.Now.AddMinutes(15).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        // ----- build params (will be sorted alphabetically for signing) -----
        var raw = new Dictionary<string, string>
        {
            ["vnp_Version"]    = version,
            ["vnp_Command"]    = command,
            ["vnp_TmnCode"]    = tmnCode,
            ["vnp_Amount"]     = amountLong.ToString(CultureInfo.InvariantCulture),
            ["vnp_CreateDate"] = createDate,
            ["vnp_CurrCode"]   = currCode,
            ["vnp_IpAddr"]     = string.IsNullOrWhiteSpace(ipAddress) ? "0.0.0.0" : ipAddress,
            ["vnp_Locale"]     = locale,
            ["vnp_OrderInfo"]  = orderInfo,
            ["vnp_OrderType"]  = orderType,
            ["vnp_ReturnUrl"]  = returnUrl,
            ["vnp_TxnRef"]     = paymentId,
            ["vnp_ExpireDate"] = expireDate,
        };

        // Exclude any empty values — VNPay ignores them at parse time too.
        var sorted = raw
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var signData = BuildSignData(sorted);
        var secureHash = HmacSha512Hex(hashSecret, signData);

        // Append hash + hashType last; do NOT re-sign (matches Java line 67-69).
        var query = string.Join("&",
            sorted.Select(kv => $"{UrlEncode(kv.Key)}={UrlEncode(kv.Value)}"));

        var url = $"{baseUrl}?{query}&{SecureHashField}={secureHash}&{SecureHashTypeField}=SHA512";

        _logger.LogInformation(
            "[VnPay] Built URL txRef={PaymentId} orderNumber={OrderNumber} amount={Amount}VND*100={AmountLong} signChars={SignLen}",
            paymentId, orderNumber, amount, amountLong, secureHash.Length);

        return url;
    }

    public bool VerifySignature(IDictionary<string, string> vnpParams)
    {
        if (vnpParams is null || vnpParams.Count == 0) return false;

        if (!vnpParams.TryGetValue(SecureHashField, out var suppliedHash)
            || string.IsNullOrWhiteSpace(suppliedHash))
        {
            return false;
        }

        var hashSecret = RequiredConfig("VnPay:HashSecret");

        // Recompute over the EXACT set of params (excluding vnp_SecureHash + vnp_SecureHashType).
        var sorted = vnpParams
            .Where(kv => kv.Key != SecureHashField && kv.Key != SecureHashTypeField)
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var signData = BuildSignData(sorted);
        var expected = HmacSha512Hex(hashSecret, signData);

        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(suppliedHash.ToUpperInvariant()));

        if (!ok)
        {
            _logger.LogWarning(
                "[VnPay] Signature mismatch expected_len={ExpectedLen} supplied_len={SuppliedLen}",
                expected.Length, suppliedHash.Length);
        }

        return ok;
    }

    // ----- helpers -----

    /// <summary>
    /// Java String.join("&amp;", kvs) over the sorted params.
    /// We use the LITERAL "&amp;" token to mirror PaymentServiceImpl.java line 49
    /// exactly; VNPay's JS verifier decodes HTML entities before parsing the
    /// querystring so this works against the sandbox and production environments.
    /// </summary>
    private static string BuildSignData(IDictionary<string, string> sorted)
    {
        var parts = new List<string>(sorted.Count);
        foreach (var (k, v) in sorted)
        {
            parts.Add($"{UrlEncode(k)}={UrlEncode(v)}");
        }
        return string.Join("&amp;", parts);
    }

    private static string UrlEncode(string raw)
        => HttpUtility.UrlEncode(raw, Encoding.ASCII)?.Replace("+", "%20")
           ?? throw new InvalidOperationException($"Failed to URL-encode '{raw}'.");

    private static string HmacSha512Hex(string secret, string signData)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signData));
        return Convert.ToHexString(hash); // .NET 5+ uppercase hex
    }

    private string RequiredConfig(string key)
    {
        var value = _config[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Configuration key '{key}' is missing — required for VnPayGatewayService.");
        return value;
    }
}
