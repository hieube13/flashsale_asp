namespace FlashSale.Infrastructure.External;

/// <summary>
/// VNPay gateway adapter — mirrors Java VnPayGatewayServiceImpl.
/// NOTE: Known bug in Java — HMAC-SHA512 uses literal "SECRET" instead of SECRET_KEY.
/// Preserve in .NET to match behavior, flag in KNOWN_DIFFERENCES.md.
/// </summary>
public interface IVnPayGatewayService
{
    /// <summary>Build VNPay redirect URL with HMAC-SHA512 signature.</summary>
    string CreatePaymentUrl(string paymentId, string orderNumber, decimal amount, string orderInfo, string ipAddress, CancellationToken ct = default);
}