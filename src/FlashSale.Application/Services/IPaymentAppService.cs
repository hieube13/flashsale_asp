namespace FlashSale.Application.Services;

/// <summary>
/// Payment service — VNPay gateway integration.
/// Mirrors Java PaymentAppService.
/// </summary>
public interface IPaymentAppService
{
    Task<string> CreatePaymentUrlAsync(long userId, string orderNumber, string method, CancellationToken ct = default);
    Task<string> BuildAndPersistPaymentAsync(long userId, string orderNumber, string method,
        string paymentUrl, string txnRef, decimal amount, CancellationToken ct = default);
    Task<VnPayIpnResponse> HandleCallbackAsync(IDictionary<string, string> vnpParams, CancellationToken ct = default);
}

/// <summary>
/// VNPay IPN response — mirrors Java VnPayIpnResponse.
/// </summary>
public sealed record VnPayIpnResponse(string RspCode, string Message);
