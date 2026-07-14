namespace FlashSale.Infrastructure.External;

/// <summary>
/// VNPay gateway adapter — mirrors Java VnPayGatewayServiceImpl.
///
/// KNOWN DIFF vs Java (kept on purpose — flagged in KNOWN_DIFFERENCES.md §10):
///  - Java hardcoded secret-key literal "SECRET" in HMAC call (bug never reached prod).
///    .NET reads it from configuration (VnPay:HashSecret) so this bug is fixed in the .NET port.
///
/// VNPay HMAC-SHA512 contract:
///  - Sign payload = concat all (key=value) sorted by key alphabetically,
///    URL-encoded with US-ASCII (HttpUtility.UrlEncode → replace '+' with "%20").
///  - Hash = Hex(HMAC-SHA512(hashSecret, signPayload)).uppercase().
///  - "vnp_SecureHash" key is excluded from the sign payload itself.
/// </summary>
public interface IVnPayGatewayService
{
    /// <summary>Build VNPay redirect URL with HMAC-SHA512 signature.</summary>
    string CreatePaymentUrl(string paymentId, string orderNumber, decimal amount, string orderInfo, string ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Verify vnp_SecureHash on an incoming set of VNPay params (Return URL or IPN).
    /// Pure: does not touch DB / Redis / network. Returns false when the secure hash
    /// is missing, malformed, or doesn't match the recomputed signature.
    /// </summary>
    bool VerifySignature(IDictionary<string, string> vnpParams);
}
