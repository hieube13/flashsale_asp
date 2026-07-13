# Module 04 — Payment (VNPay)

## Java → .NET

| Java | .NET |
|------|------|
| `com.xxxx.ddd.controller.http.PaymentController` | `FlashSale.Api.Controllers.PaymentController` |
| `com.xxxx.ddd.application.service.payment.PaymentAppService` | `FlashSale.Application.Services.IPaymentAppService` |
| `com.xxxx.ddd.application.service.payment.impl.PaymentAppServiceImpl` | `FlashSale.Application.Services.Implementations.PaymentAppServiceImpl` |
| `com.xxxx.ddd.infrastructure.gateway.VnPayGatewayServiceImpl` | `FlashSale.Infrastructure.External.VnPayGatewayService` |

## Endpoints

| Method | Route | Behaviour |
|--------|-------|-----------|
| POST | `/payment/create?userId=&orderNumber=&method=VNPAY` | Create payment transaction, return VNPay redirect URL |
| POST | `/payment/vnpay/callback` | VNPay IPN callback (verify signature, update status) |

## Algorithms

### Create payment URL

```
1. Extract yearMonth from orderNumber (substring trick — see Java line 37)
2. Lookup order by orderNumber → if not PENDING (status=0) → error
3. Insert payment_transaction (INIT)
4. Build VNPay URL via VnPayGatewayService.createPaymentUrl(...)
5. UPDATE payment_transaction SET status=IN_PROGRESS, payment_url=...
6. Return URL
```

### VNPay URL build

- vnp_Version=2.1.0, vnp_Command=pay, vnp_TmnCode (config), vnp_Amount (×100), vnp_CurrCode=VND
- vnp_TxnRef = last 12 chars of paymentId (UUID truncation)
- vnp_OrderType=other, vnp_OrderInfo="Thanh toan don hang: {orderNumber}"
- vnp_Locale=vn, vnp_ReturnUrl (config), vnp_IpAddr=127.0.0.1
- vnp_CreateDate=yyyyMMddHHmmss
- **Build hashData**: TreeMap ordering, URL-encode field names + values (replace `+` with `%20`)
- **Hash**: HMAC-SHA512 with secret → append as `&vnp_SecureHash=...`

### Critical Java bug (KNOWN_DIFFERENCES §1)

Java line 59: `String vnp_SecureHash = hmacSHA512("SECRET", hashDataStr);`
Hardcoded literal `"SECRET"` — every generated URL has the same hash, callback verify will fail.

**.NET fix**: read `VnPay:SecretKey` from config, default in `.env.example` is `REPLACE_WITH_VNPAY_SANDBOX_SECRET`.

## Tasks

- **TASK-018**: payment_vnpay — HMAC-SHA512 + URL builder + callback verify

## Config

```json
{
  "VnPay": {
    "SecretKey": "REPLACE_WITH_VNPAY_SANDBOX_SECRET",
    "TmnCode": "VNPAYSANDBOX",
    "Url": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
    "ReturnUrl": "http://127.0.0.1:8080/payment/callback"
  }
}
```

## Tables

| Table | Purpose |
|-------|---------|
| `payment_transaction` | One row per payment attempt |

State machine: INIT (0) → IN_PROGRESS (1) → SUCCESS (2) / FAILED (3).