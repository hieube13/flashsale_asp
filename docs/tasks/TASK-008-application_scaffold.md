# TASK-008 — application_scaffold

| Field | Value |
|-------|-------|
| Status | ✅ done |
| Branch | — |
| Module | application |
| Phase | 0 — Scaffold |
| Commit | — |
| Completed | 2026-07-13 |

## Mục tiêu

9 service interfaces tương ứng 9 module Java. Application layer pure (no infra deps).

## Tệp Java nguồn (chỉ đọc)

- `xxxx-application/.../application/service/ticket/TicketAppService.java`
- `xxxx-application/.../application/service/ticket/TicketDetailAppService.java`
- `xxxx-application/.../application/service/order/TicketOrderAppService.java`
- `xxxx-application/.../application/service/order/mq/OrderMQAppService.java`
- `xxxx-application/.../application/service/payment/PaymentAppService.java`
- `xxxx-application/.../application/service/booking/BookingAppService.java`
- `xxxx-application/.../application/service/employee/cache/EmployeeCacheService.java`
- `xxxx-application/.../application/service/event/EventAppService.java`

## File .NET đích (đã tạo)

- `src/FlashSale.Application/Services/ITicketAppService.cs` (+ ITicketDetailAppService)
- `src/FlashSale.Application/Services/ITicketOrderAppService.cs`
- `src/FlashSale.Application/Services/IOrderMqAppService.cs` (+ IOrderMqConsumerHandler)
- `src/FlashSale.Application/Services/IPaymentAppService.cs`
- `src/FlashSale.Application/Services/IBookingAppService.cs` (+ IEmployeeCacheService, IEventAppService)

## Checklist

- [x] All interfaces `async Task<...>` returning standard types
- [x] CancellationToken on every method
- [x] Method signatures mirror Java 1-1 (different naming where .NET idiom requires)
- [x] No infrastructure deps (Application references Domain + Contracts only)
- [x] Build pass

## Verification

```powershell
dotnet build src/FlashSale.Application/FlashSale.Application.csproj
```