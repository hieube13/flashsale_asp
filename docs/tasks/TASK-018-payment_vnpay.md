# TASK-018 — payment_vnpay

| Field | Value |
|-------|-------|
| Status | 🟡 pending |
| Branch | `f_task_018_payment_vnpay` |
| Module | payment |
| Phase | 1 — Feature port |
| Commit | — |
| Completed | — |

## Mục tiêu

Port VNPay payment flow: create URL với HMAC-SHA512, callback handler xác thực chữ ký, cập nhật payment_transaction status.

## Tệp Java nguồn (chỉ đọc)

- `xxxx-controller/.../controller/http/PaymentController.java` — `POST /payment/create`
- `xxxx-application/.../application/service/payment/PaymentAppService.java` + `Impl`
- `xxxx-infrastructure/.../infrastructure/gateway/VnPayGatewayServiceImpl.java` — **contains the "SECRET" bug**
- `xxxx-controller/.../controller/exception/InvalidSignatureException.java`

## File .NET đích (sẽ tạo)

- `src/FlashSale.Infrastructure/External/VnPayGatewayService.cs` — full HMAC-SHA512 impl
- `src/FlashSale.Application/Services/Implementations/PaymentAppServiceImpl.cs`
- `src/FlashSale.Infrastructure/Persistence/Repositories/PaymentRepositoryImpl.cs`
- `src/FlashSale.Api/Controllers/PaymentController.cs` (create + callback)

## VnPayGatewayService.createPaymentUrl

Mirror Java exactly — TreeMap ordering, US-ASCII encoding, replace `+` with `%20`, append `&vnp_SecureHash=<hmac>`.

### Critical: Java bug fix decision

Java line 59: `String vnp_SecureHash = hmacSHA512("SECRET", hashDataStr);`
Hardcoded literal `"SECRET"` instead of the constant `SECRET_KEY`.

**Decision** (record in KNOWN_DIFFERENCES §1):
- `.NET` reads secret from `VnPay:SecretKey` config
- Default in `.env.example` = `REPLACE_WITH_VNPAY_SANDBOX_SECRET`
- This is a **fix** (not preserve) because callback verify will fail otherwise

## Endpoints

| Method | Route | Behaviour |
|--------|-------|-----------|
| POST | `/payment/create?userId=&orderNumber=&method=VNPAY` | Build VNPay URL, insert PaymentTransaction(IN_PROGRESS), return URL |
| POST | `/payment/vnpay/callback` (or `/payment/callback`) | Verify signature, update status to SUCCESS/FAILED |

## Acceptance criteria

- [ ] HMAC-SHA512 matches Java byte-for-byte for same input
- [ ] URL encoding matches Java exactly (US-ASCII + replace `+` with `%20`)
- [ ] TreeMap ordering preserved
- [ ] Callback signature verify uses same hash algorithm
- [ ] PaymentTransaction state machine: INIT → IN_PROGRESS → SUCCESS/FAILED
- [ ] Update KNOWN_DIFFERENCES.md with verdict on SECRET bug fix
- [ ] Unit test: known Java-generated URL can be reproduced by .NET (golden URL fixture)
- [ ] Integration test: full round-trip create → callback

## Verification

```powershell
dotnet build FlashSale.slnx
dotnet test tests/FlashSale.UnitTests --filter "FullyQualifiedName~Payment"
dotnet test tests/FlashSale.IntegrationTests --filter "FullyQualifiedName~Payment"

# Smoke
curl -X POST "http://localhost:5080/payment/create?userId=1001&orderNumber=ORD2026020001&method=VNPAY"
```

## Suggested commit

```
[TASK-018] payment_vnpay: hmac-sha512 url builder + callback signature verify + state machine
```