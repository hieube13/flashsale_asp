# TASK-014 — order_cancel_slice

| Field | Value |
|-------|-------|
| Status | 🟡 pending |
| Branch | `f_task_014_order_cancel_slice` |
| Module | order |
| Phase | 1 — Feature port |
| Commit | — |
| Completed | — |

## Mục tiêu

Port `cancelOrder` — distributed lock theo orderNumber, update DB status, restore Redis stock.

## Tệp Java nguồn (chỉ đọc)

- `xxxx-application/.../application/service/order/impl/TicketOrderAppServiceImpl.java:439-511` — `cancelOrder`
- `xxxx-infrastructure/.../infrastructure/distributed/redisson/impl/RedisDistributedLockerImpl.java`
- `xxxx-application/.../application/service/order/cache/StockOrderCacheService.java` — `increaseStockCache`

## File .NET đích (sẽ tạo)

- `src/FlashSale.Infrastructure/DistributedLock/RedLockDistributedLockProvider.cs` — full impl (RedLock.net)
- `src/FlashSale.Application/Services/Implementations/TicketOrderCancelService.cs` (hoặc thêm method vào TaskOrderAppServiceImpl)
- Update TicketOrderController: `PUT /order/{userId}/{orderNumber}/cancel`

## Lock flow

```
1. Acquire RedLock on key = "LOCK:CANCEL_ORDER:{orderNumber}"
   waitTime=1s, leaseTime=5s
   if not acquired → return false
2. Extract yearMonth from orderNumber
3. Find order by orderNumber → if null OR not owner → unlock + return false
4. If orderStatus == 2 (already cancelled) → unlock + return true
5. UPDATE ticket_order_{yyyyMM} SET order_status = 2 WHERE order_number = ?
6. Increase Redis stock: PRO_TICKET:{ticketId}:stock_available += quantity
7. If Redis increase fails → log warning, return success anyway (DB is source of truth)
8. Unlock in finally
```

## Acceptance criteria

- [ ] Lock acquired via RedLock with `wait=1s, lease=5s`
- [ ] `finally` block always releases lock (try/finally)
- [ ] DB status update is atomic (single UPDATE statement)
- [ ] Redis restore is best-effort (warning logged on failure)
- [ ] Idempotent: re-cancel returns true
- [ ] Unit test: simulate lock contention, only one caller succeeds
- [ ] Integration test: end-to-end cancel via HTTP

## Verification

```powershell
dotnet build FlashSale.slnx
dotnet test tests/FlashSale.UnitTests --filter "FullyQualifiedName~Cancel"
dotnet test tests/FlashSale.IntegrationTests --filter "FullyQualifiedName~Cancel"

# Smoke
curl -X PUT "http://localhost:5080/order/1001/ORD2026020001/cancel"
```

## Suggested commit

```
[TASK-014] order_cancel_slice: redlock + db status update + redis restore
```