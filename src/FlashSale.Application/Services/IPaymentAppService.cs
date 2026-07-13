namespace FlashSale.Application.Services;

/// <summary>
/// Payment service — VNPay gateway integration.
/// Mirrors Java PaymentAppService.
/// </summary>
public interface IPaymentAppService
{
    Task<string> CreatePaymentUrlAsync(long userId, string orderNumber, string method, CancellationToken ct = default);
    Task HandleCallbackAsync(IDictionary<string, string> vnpParams, CancellationToken ct = default);
}