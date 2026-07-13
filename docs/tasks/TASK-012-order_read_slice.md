# TASK-012 — order_read_slice

| Field | Value |
|-------|-------|
| Status | 🟡 pending |
| Branch | `f_task_012_order_read_slice` |
| Module | catalog (read) |
| Phase | 1 — Feature port |
| Commit | — |
| Completed | — |

## Mục tiêu

Port các method read của `TicketOrderAppServiceImpl`:

- `findAll(yearMonth)` — load toàn bộ orders trong tháng (V1, no pagination)
- `findPage(yearMonth, cursor, limit)` — cursor-based pagination
- `findByOrderNumber(yearMonth, orderNumber)` — lookup by orderNumber

Dùng **Dapper** vì table name động (`ticket_order_yyyyMM`).

## Tệp Java nguồn (chỉ đọc)

- `xxxx-application/.../application/service/order/impl/TicketOrderAppServiceImpl.java:259-388`
- `xxxx-domain/.../domain/service/OrderDeductionDomainService.java` + `Impl`
- `xxxx-application/.../application/model/TicketOrderDTO.java`
- `xxxx-application/.../application/model/PagedOrdersDTO.java`

## File .NET đích (sẽ tạo)

- `src/FlashSale.Infrastructure/Persistence/Repositories/TickerOrderRepositoryDapper.cs` — Dapper-based, dynamic table
- `src/FlashSale.Application/Services/Implementations/TicketOrderReadService.cs` — read-side orchestration (or extend `TicketOrderAppServiceImpl` once TASK-013 lands)
- `src/FlashSale.Application/Mappers/TicketOrderMapper.cs` — `Object[]` → `TicketOrderDto`
- `src/FlashSale.Domain/Services/OrderDeductionDomainService.cs` — extract yearMonth from orderNumber

## Dynamic table logic

```csharp
// Extract yearMonth from orderNumber
// Java: orderNumber = "OKX-SGN-{userId}-{seq}-{tsMillis}"
// Last segment is UnixMillis → convert to yyyyMM
private static string ExtractYearMonth(string orderNumber)
{
    var lastDash = orderNumber.LastIndexOf('-');
    var tsStr = orderNumber[(lastDash + 1)..];
    var ts = long.Parse(tsStr);
    return DateTimeOffset.FromUnixTimeMilliseconds(ts)
        .ToLocalTime()
        .ToString("yyyyMM");
}
```

## Endpoints to mirror

| Method | Route | Behaviour |
|--------|-------|-----------|
| GET | `/order/{userId}/list?ntable=yyyyMM` | All orders in that month |
| GET | `/order/{userId}/list/page?ntable=yyyyMM&cursor=lastId&limit=50` | Paged, max 100 |
| GET | `/order/{userId}/{orderNumber}` | Lookup single order (table derived from orderNumber) |

## Acceptance criteria

- [ ] Dapper dynamic-table query: `SELECT * FROM ticket_order_{yyyyMM}` (validate table exists first)
- [ ] Cursor pagination returns `nextCursor = lastId` when `result.size == limit`
- [ ] `findByOrderNumber` extracts yearMonth from orderNumber correctly
- [ ] All queries return `TicketOrderDto` (12 columns mapped from `Object[]`)
- [ ] Unit tests for cursor logic + yearMonth extraction
- [ ] Integration tests with Testcontainers MySQL + pre-seeded `ticket_order_202604` table

## Verification

```powershell
dotnet build FlashSale.slnx
dotnet test tests/FlashSale.UnitTests --filter "FullyQualifiedName~OrderRead"
dotnet test tests/FlashSale.IntegrationTests --filter "FullyQualifiedName~OrderRead"

# Smoke
curl "http://localhost:5080/order/1001/list?ntable=202604"
curl "http://localhost:5080/order/1001/list/page?ntable=202604&cursor=0&limit=50"
```

## Suggested commit

```
[TASK-012] order_read_slice: dapper dynamic table + cursor pagination + findByOrderNumber
```