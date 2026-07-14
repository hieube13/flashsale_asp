using FlashSale.Application.Services;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Services.Implementations;

/// <summary>
/// Payment app service — TASK-018 (port of Java PaymentServiceImpl).
///
/// Two surfaces:
///   - <see cref="BuildAndPersistPaymentAsync"/> — orchestration only. Takes the
///     already-built <paramref name="paymentUrl"/> from the controller (the
///     <c>IVnPayGatewayService</c> lives in Infrastructure and cannot be referenced
///     from Application per the architecture graph). The controller is
///     responsible for HMAC signing + IP resolution; we only handle idempotency
///     lookup + DB persist + amount recovery. Mirrors Java
///     <c>PaymentServiceImpl.createPayment(...)</c>.
///   - <see cref="HandleCallbackAsync"/> — caller hits POST /payment/callback/ipn.
///     Acquires distributed lock <c>LOCK:PAYMENT_IPN:{txnRef}</c> so concurrent
///     VNPay retries for the same txnRef only run once; updates PaymentTransaction
///     row by status (PENDING → SUCCESS/FAILED). Does NOT touch ticket_order
///     (matches Java — see KNOWN_DIFFERENCES.md §11).
///
/// Pure orchestration; HMAC-SHA512 signing lives in
/// <c>FlashSale.Infrastructure.External.VnPayGatewayService</c>.
/// </summary>
public sealed class PaymentAppServiceImpl : IPaymentAppService
{
    private readonly IPaymentRepository _payments;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<PaymentAppServiceImpl> _log;

    // Payment status constants (mirrors Java enum order).
    private const int StatusInit       = 0;
    private const int StatusInProgress = 1;
    private const int StatusSuccess    = 2;
    private const int StatusFailed     = 3;

    public PaymentAppServiceImpl(
        IPaymentRepository payments,
        IDistributedLockProvider lockProvider,
        ILogger<PaymentAppServiceImpl> log)
    {
        _payments = payments;
        _lockProvider = lockProvider;
        _log = log;
    }

    /// <summary>
    /// IPaymentAppService entrypoint — kept for interface parity but unused by
    /// the controller. The controller calls
    /// <see cref="BuildAndPersistPaymentAsync"/> directly.
    /// </summary>
    public Task<string> CreatePaymentUrlAsync(long userId, string orderNumber, string method, CancellationToken ct = default)
        => throw new NotSupportedException(
            "PaymentController should call BuildAndPersistPaymentAsync directly — " +
            "the IVnPayGatewayService lives in Infrastructure and is not visible from Application.");

    /// <summary>
    /// Idempotency lookup + persist. The controller builds the URL via the
    /// gateway (HMAC-SHA512 lives there) and passes the result here. Returns
    /// the URL the controller should return to the client — which is either a
    /// fresh one or, when a PENDING row already exists for the same
    /// (user, orderNumber), the existing one.
    /// </summary>
    public async Task<string> BuildAndPersistPaymentAsync(
        long userId,
        string orderNumber,
        string method,
        string paymentUrl,
        string txnRef,
        decimal amount,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new ArgumentException("orderNumber required", nameof(orderNumber));
        if (string.IsNullOrWhiteSpace(paymentUrl))
            throw new ArgumentException("paymentUrl required", nameof(paymentUrl));
        if (string.IsNullOrWhiteSpace(txnRef))
            throw new ArgumentException("txnRef required", nameof(txnRef));

        // 1. Idempotency — reuse PENDING/INIT row for the same (user, order).
        var existing = await _payments.FindByOrderNumberAsync(orderNumber, ct);
        var pendingForUser = existing.FirstOrDefault(p =>
            p.UserId == (int)userId &&
            (p.PaymentStatus == StatusInit || p.PaymentStatus == StatusInProgress) &&
            !string.IsNullOrEmpty(p.PaymentUrl));

        if (pendingForUser is not null)
        {
            _log.LogInformation(
                "[Payment] Reusing PENDING paymentId={PaymentId} orderNumber={OrderNumber}",
                pendingForUser.PaymentId, orderNumber);
            return pendingForUser.PaymentUrl!;
        }

        // 2. Persist as IN_PROGRESS.
        var tx = new PaymentTransaction
        {
            PaymentId = txnRef,
            OrderNumber = orderNumber,
            UserId = (int)userId,
            Amount = amount,
            PaymentMethod = string.IsNullOrWhiteSpace(method) ? "VNPAY" : method,
            PaymentStatus = StatusInProgress,
            PaymentUrl = paymentUrl,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await _payments.CreateAsync(tx, ct);

        _log.LogInformation(
            "[Payment] Created txnRef={TxnRef} orderNumber={OrderNumber} amount={Amount}",
            txnRef, orderNumber, amount);

        return paymentUrl;
    }

    public async Task<VnPayIpnResponse> HandleCallbackAsync(IDictionary<string, string> vnpParams, CancellationToken ct = default)
    {
        // 1. Pull the txnRef. VNPay sends it as vnp_TxnRef.
        if (!vnpParams.TryGetValue("vnp_TxnRef", out var txnRef) || string.IsNullOrWhiteSpace(txnRef))
        {
            _log.LogWarning("[Payment/IPN] Missing vnp_TxnRef");
            return new VnPayIpnResponse("01", "Order not found");
        }

        // 2. Response code from VNPay: "00" = success. Anything else is a failure.
        var vnpResponseCode = vnpParams.TryGetValue("vnp_ResponseCode", out var rc) ? rc : "99";
        var vnpTransactionNo = vnpParams.TryGetValue("vnp_TransactionNo", out var txn) ? txn : null;
        var vnpAmount = vnpParams.TryGetValue("vnp_Amount", out var amt) ? amt : "0";

        // 3. Acquire distributed lock (q3 — RedLock per txnRef mirrors TASK-014 CancelOrderAsync).
        var lockKey = $"LOCK:PAYMENT_IPN:{txnRef}";
        var handle = _lockProvider.GetLock(lockKey);
        var acquired = await handle.TryAcquireAsync(
            expiry: TimeSpan.FromSeconds(10),
            wait:   TimeSpan.FromSeconds(5),
            ct);

        if (!acquired)
        {
            _log.LogWarning("[Payment/IPN] Lock busy for txnRef={TxnRef} (concurrent retry)", txnRef);
            return new VnPayIpnResponse("02", "Order already confirmed");
        }

        try
        {
            // 4. Load the payment row.
            var tx = await _payments.GetByPaymentIdAsync(txnRef, ct);
            if (tx is null)
            {
                _log.LogWarning("[Payment/IPN] Unknown txnRef={TxnRef}", txnRef);
                return new VnPayIpnResponse("01", "Order not found");
            }

            // 5. Already terminal? Idempotent: confirm success regardless of code.
            if (tx.PaymentStatus == StatusSuccess || tx.PaymentStatus == StatusFailed)
            {
                _log.LogInformation(
                    "[Payment/IPN] txnRef={TxnRef} already terminal status={Status} — idempotent ACK",
                    txnRef, tx.PaymentStatus);
                return new VnPayIpnResponse("00", "Confirm Success");
            }

            // 6. Validate amount matches the row.
            if (!decimal.TryParse(vnpAmount, System.Globalization.NumberStyles.Any,
                                  System.Globalization.CultureInfo.InvariantCulture,
                                  out var incomingLong))
            {
                _log.LogWarning(
                    "[Payment/IPN] Bad amount format txnRef={TxnRef} raw={Raw}", txnRef, vnpAmount);
                return new VnPayIpnResponse("04", "Invalid amount");
            }

            // Stored amount is in VND; gateway amount is VND * 100.
            var expectedLong = (long)Math.Round(tx.Amount * 100m, MidpointRounding.AwayFromZero);
            if (incomingLong != expectedLong)
            {
                _log.LogWarning(
                    "[Payment/IPN] Amount mismatch txnRef={TxnRef} expected={Expected} got={Got}",
                    txnRef, expectedLong, incomingLong);
                return new VnPayIpnResponse("04", "Invalid amount");
            }

            // 7. Flip status based on responseCode.
            var newStatus = vnpResponseCode == "00" ? StatusSuccess : StatusFailed;
            await _payments.UpdateStatusAsync(txnRef, newStatus, vnpTransactionNo, ct);

            _log.LogInformation(
                "[Payment/IPN] txnRef={TxnRef} status {From}->{To} amount={Amount}",
                txnRef, tx.PaymentStatus, newStatus, tx.Amount);

            return new VnPayIpnResponse("00", "Confirm Success");
        }
        finally
        {
            await handle.ReleaseAsync(ct);
        }
    }
}
