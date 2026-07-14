using FlashSale.Application.Services;
using FlashSale.Application.Services.Implementations;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlashSale.UnitTests.Application;

/// <summary>
/// Unit tests for <see cref="PaymentAppServiceImpl"/> — TASK-018.
///
/// Scope (mirrors TASK-014 CancelOrder test pattern):
///   - Happy path: PENDING row + RspCode=00 → status flips to SUCCESS, IPN={00,"Confirm Success"}.
///   - Bad signature is rejected up-front by the controller; we don't reach here when it's bad.
///   - Idempotent: terminal state (SUCCESS or FAILED) → still return {00,"Confirm Success"} so VNPay stops retrying.
///   - Amount mismatch → IPN={04,"Invalid amount"}; row stays PENDING.
///   - Lock busy → IPN={02,"Order already confirmed"} without touching the row.
///   - Missing txnRef → IPN={01,"Order not found"}.
///   - Unknown txnRef → IPN={01,"Order not found"}.
///   - RspCode != 00 (e.g. "24") → status flips to FAILED, IPN={00,"Confirm Success"}.
///   - Idempotency lookup on BuildAndPersistPayment: existing PENDING row returns the OLD URL.
/// </summary>
public class PaymentAppServiceImplTests
{
    private const string TxnRef = "abc123";
    private const string OrderNumber = "OKX-SGN-7-1-1721000000000";
    private const int UserId = 7;

    private static readonly DateTime Utc = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);

    private sealed class LockHarness
    {
        public Mock<IDistributedLockProvider> Provider { get; }
        public Mock<IDistributedLock> Handle { get; }
        public LockHarness(bool acquired)
        {
            Provider = new Mock<IDistributedLockProvider>(MockBehavior.Strict);
            Handle   = new Mock<IDistributedLock>(MockBehavior.Strict);
            Handle.Setup(h => h.TryAcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(acquired);
            Handle.Setup(h => h.ReleaseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Provider.Setup(p => p.GetLock(It.IsAny<string>())).Returns(Handle.Object);
        }
    }

    private static PaymentAppServiceImpl BuildSut(
        Mock<IPaymentRepository> payments, LockHarness locks)
        => new(payments.Object, locks.Provider.Object,
               NullLogger<PaymentAppServiceImpl>.Instance);

    private static PaymentTransaction Sample(int status, decimal amount = 100000m)
        => new()
        {
            Id = 1,
            PaymentId = TxnRef,
            OrderNumber = OrderNumber,
            UserId = UserId,
            Amount = amount,
            PaymentMethod = "VNPAY",
            PaymentStatus = status,
            PaymentUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?existing",
            CreatedAt = Utc,
            UpdatedAt = Utc,
        };

    private static IDictionary<string, string> VnpParams(
        string responseCode = "00", string? amountCents = "10000000", string txnRef = TxnRef,
        string? transactionNo = "9876543210")
    {
        var d = new Dictionary<string, string>
        {
            ["vnp_TmnCode"]       = "DEMOTMN",
            ["vnp_TxnRef"]        = txnRef,
            ["vnp_Amount"]        = amountCents ?? string.Empty,
            ["vnp_OrderInfo"]     = "info",
            ["vnp_ResponseCode"]  = responseCode,
            ["vnp_TransactionNo"] = transactionNo ?? string.Empty,
        };
        return d;
    }

    // ============== Happy path ==============

    [Fact]
    public async Task HandleCallbackAsync_happy_path_flips_PENDING_to_SUCCESS()
    {
        var payments = new Mock<IPaymentRepository>(MockBehavior.Strict);
        var locks = new LockHarness(true);
        var sut = BuildSut(payments, locks);

        payments.Setup(p => p.GetByPaymentIdAsync(TxnRef, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Sample(status: 1));
        payments.Setup(p => p.UpdateStatusAsync(TxnRef, 2, "9876543210", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var result = await sut.HandleCallbackAsync(VnpParams(responseCode: "00"), CancellationToken.None);

        Assert.Equal("00", result.RspCode);
        Assert.Equal("Confirm Success", result.Message);
        payments.Verify(p => p.UpdateStatusAsync(TxnRef, 2, "9876543210", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ============== Idempotency ==============

    [Fact]
    public async Task HandleCallbackAsync_already_SUCCESS_is_idempotent_no_DB_write()
    {
        var payments = new Mock<IPaymentRepository>(MockBehavior.Strict);
        var locks = new LockHarness(true);
        var sut = BuildSut(payments, locks);

        payments.Setup(p => p.GetByPaymentIdAsync(TxnRef, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Sample(status: 2));

        var result = await sut.HandleCallbackAsync(VnpParams(responseCode: "24"), CancellationToken.None);

        Assert.Equal("00", result.RspCode);
        Assert.Equal("Confirm Success", result.Message);
        payments.Verify(p => p.GetByPaymentIdAsync(TxnRef, It.IsAny<CancellationToken>()), Times.Once);
        payments.Verify(p => p.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(),
                                                It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                        Times.Never);
    }

    [Fact]
    public async Task HandleCallbackAsync_already_FAILED_is_idempotent_no_DB_write()
    {
        var payments = new Mock<IPaymentRepository>(MockBehavior.Strict);
        var locks = new LockHarness(true);
        var sut = BuildSut(payments, locks);

        payments.Setup(p => p.GetByPaymentIdAsync(TxnRef, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Sample(status: 3));

        var result = await sut.HandleCallbackAsync(VnpParams(responseCode: "24"), CancellationToken.None);
        Assert.Equal("00", result.RspCode);
        payments.Verify(p => p.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(),
                                                It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                        Times.Never);
    }

    // ============== Validation ==============

    [Fact]
    public async Task HandleCallbackAsync_amount_mismatch_returns_04()
    {
        var payments = new Mock<IPaymentRepository>(MockBehavior.Strict);
        var locks = new LockHarness(true);
        var sut = BuildSut(payments, locks);

        payments.Setup(p => p.GetByPaymentIdAsync(TxnRef, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Sample(status: 1, amount: 100_000m));

        var result = await sut.HandleCallbackAsync(VnpParams(amountCents: "20000000"), CancellationToken.None);

        Assert.Equal("04", result.RspCode);
        Assert.Equal("Invalid amount", result.Message);
        payments.Verify(p => p.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(),
                                                It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                        Times.Never);
    }

    [Fact]
    public async Task HandleCallbackAsync_unknown_txnRef_returns_01()
    {
        var payments = new Mock<IPaymentRepository>(MockBehavior.Strict);
        var locks = new LockHarness(true);
        var sut = BuildSut(payments, locks);

        payments.Setup(p => p.GetByPaymentIdAsync(TxnRef, It.IsAny<CancellationToken>()))
                .ReturnsAsync((PaymentTransaction?)null);

        var result = await sut.HandleCallbackAsync(VnpParams(), CancellationToken.None);

        Assert.Equal("01", result.RspCode);
        Assert.Equal("Order not found", result.Message);
        payments.Verify(p => p.GetByPaymentIdAsync(TxnRef, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleCallbackAsync_missing_txnRef_returns_01()
    {
        var payments = new Mock<IPaymentRepository>(MockBehavior.Strict);
        var locks = new LockHarness(true);
        var sut = BuildSut(payments, locks);

        var bad = new Dictionary<string, string>
        {
            ["vnp_TmnCode"]      = "DEMOTMN",
            ["vnp_ResponseCode"] = "00",
            ["vnp_Amount"]       = "10000000",
        };

        var result = await sut.HandleCallbackAsync(bad, CancellationToken.None);
        Assert.Equal("01", result.RspCode);
        Assert.Equal("Order not found", result.Message);
        payments.Verify(p => p.GetByPaymentIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                        Times.Never);
    }

    // ============== Lock busy ==============

    [Fact]
    public async Task HandleCallbackAsync_lock_busy_returns_02_without_DB()
    {
        var payments = new Mock<IPaymentRepository>(MockBehavior.Strict);
        var locks = new LockHarness(false);
        var sut = BuildSut(payments, locks);

        var result = await sut.HandleCallbackAsync(VnpParams(), CancellationToken.None);

        Assert.Equal("02", result.RspCode);
        Assert.Equal("Order already confirmed", result.Message);
        payments.Verify(p => p.GetByPaymentIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                        Times.Never);
    }

    // ============== Failure code path ==============

    [Fact]
    public async Task HandleCallbackAsync_responseCode_24_flips_to_FAILED()
    {
        var payments = new Mock<IPaymentRepository>(MockBehavior.Strict);
        var locks = new LockHarness(true);
        var sut = BuildSut(payments, locks);

        payments.Setup(p => p.GetByPaymentIdAsync(TxnRef, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Sample(status: 1));
        payments.Setup(p => p.UpdateStatusAsync(TxnRef, 3, "9876543210", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var result = await sut.HandleCallbackAsync(VnpParams(responseCode: "24"), CancellationToken.None);

        Assert.Equal("00", result.RspCode);
        payments.Verify(p => p.UpdateStatusAsync(TxnRef, 3, "9876543210", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ============== Idempotency lookup on Create ==============

    [Fact]
    public async Task BuildAndPersistPaymentAsync_returns_existing_PENDING_URL_no_new_insert()
    {
        var payments = new Mock<IPaymentRepository>(MockBehavior.Strict);
        var sut = BuildSut(payments, new LockHarness(true));

        var existing = Sample(status: 1);
        payments.Setup(p => p.FindByOrderNumberAsync(OrderNumber, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { existing });

        var url = await sut.BuildAndPersistPaymentAsync(
            UserId, OrderNumber, "VNPAY",
            paymentUrl: "https://fresh.url/?next",
            txnRef: Guid.NewGuid().ToString("N"),
            amount: 100000m);

        Assert.Equal(existing.PaymentUrl, url);
        payments.Verify(p => p.CreateAsync(It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()),
                        Times.Never);
        payments.Verify(p => p.FindByOrderNumberAsync(OrderNumber, It.IsAny<CancellationToken>()),
                        Times.Once);
    }

    [Fact]
    public async Task BuildAndPersistPaymentAsync_creates_new_row_when_no_PENDING_exists()
    {
        var payments = new Mock<IPaymentRepository>(MockBehavior.Strict);
        var sut = BuildSut(payments, new LockHarness(true));

        var url = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?fresh=1";
        var txn = Guid.NewGuid().ToString("N");

        payments.Setup(p => p.FindByOrderNumberAsync(OrderNumber, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<PaymentTransaction>());
        payments.Setup(p => p.CreateAsync(It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((PaymentTransaction x, CancellationToken _y) => x);

        var returned = await sut.BuildAndPersistPaymentAsync(
            UserId, OrderNumber, "VNPAY", url, txn, 150000m);

        Assert.Equal(url, returned);
        payments.Verify(p => p.CreateAsync(It.Is<PaymentTransaction>(t =>
            t.PaymentId == txn &&
            t.PaymentStatus == 1 &&
            t.Amount == 150000m), It.IsAny<CancellationToken>()), Times.Once);
    }
}
