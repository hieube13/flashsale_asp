using FlashSale.Infrastructure.External;

namespace FlashSale.Infrastructure.External;

/// <summary>
/// VNPay stub — HMAC-SHA512 signing added in TASK-018.
/// Port of Java VnPayGatewayServiceImpl.
/// NOTE: Java uses literal "SECRET" instead of SECRET_KEY — preserved here.
/// </summary>
public sealed class VnPayGatewayService : IVnPayGatewayService
{
    public string CreatePaymentUrl(string paymentId, string orderNumber, decimal amount, string orderInfo, string ipAddress, CancellationToken ct = default)
    {
        // Concrete HMAC-SHA512 + URL builder added in TASK-018.
        // For now return deterministic stub URL.
        return $"https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?paymentId={paymentId}&orderNumber={orderNumber}&amount={amount}";
    }
}