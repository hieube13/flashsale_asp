# TASK-004 — domain_entities

| Field | Value |
|-------|-------|
| Status | ✅ done |
| Branch | — |
| Module | domain |
| Phase | 0 — Scaffold |
| Commit | — |
| Completed | 2026-07-13 |

## Mục tiêu

Port 8 entities + 3 enums + 8 repository interfaces từ Java `xxxx-domain`.

## Tệp Java nguồn (chỉ đọc)

- `xxxx-domain/.../domain/model/entity/Ticket.java`
- `xxxx-domain/.../domain/model/entity/TicketDetail.java` (table `ticket_item`)
- `xxxx-domain/.../domain/model/entity/TickerOrder.java`
- `xxxx-domain/.../domain/model/entity/OrderQueue.java`
- `xxxx-domain/.../domain/model/entity/OutboxEvent.java`
- `xxxx-domain/.../domain/model/entity/IdempotencyKey.java`
- `xxxx-domain/.../domain/model/entity/PaymentTransaction.java`
- `xxxx-domain/.../domain/model/entity/Booking.java`

## File .NET đích (đã tạo)

- `src/FlashSale.Domain/Entities/Ticket.cs`
- `src/FlashSale.Domain/Entities/TicketDetail.cs`
- `src/FlashSale.Domain/Entities/TickerOrder.cs`
- `src/FlashSale.Domain/Entities/OrderQueue.cs`
- `src/FlashSale.Domain/Entities/OutboxEvent.cs`
- `src/FlashSale.Domain/Entities/IdempotencyKey.cs`
- `src/FlashSale.Domain/Entities/PaymentTransaction.cs`
- `src/FlashSale.Domain/Entities/Booking.cs`
- `src/FlashSale.Domain/Enums/OrderStatus.cs` (0..4)
- `src/FlashSale.Domain/Enums/OrderQueueStatus.cs` (0..2)
- `src/FlashSale.Domain/Enums/OutboxStatus.cs` (0..1)
- `src/FlashSale.Domain/Repositories/IRepositories.cs` — 8 interface bundled
- `src/FlashSale.Domain/Services/IOrderDeductionDomainService.cs`

## Checklist

- [x] 8 entity classes, properties mirror Java fields
- [x] Status int constants match Java (Ticket: 0/1/2, Order: 0/1/2/3/4, Queue: 0/1/2, Outbox: 0/1, Payment: 0/1/2/3, Booking: 0/1/2)
- [x] 8 repository interfaces (Ticker, TicketDetail, TickerOrder, OrderQueue, OutboxEvent, IdempotencyKey, Payment, Booking)
- [x] Build pass, Domain references nothing

## Verification

```powershell
dotnet build src/FlashSale.Domain/FlashSale.Domain.csproj
# Build succeeded. 0 Warning(s) 0 Error(s)
```