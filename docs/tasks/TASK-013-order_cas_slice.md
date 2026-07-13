# TASK-013 — order_cas_slice

| Field | Value |
|-------|-------|
| Status | 🟡 pending |
| Branch | `f_task_013_order_cas_slice` |
| Module | order |
| Phase | 1 — Feature port |
| Commit | — |
| Completed | — |

## Mục tiêu

Đây là **task quan trọng nhất** — port logic CAS atomic trong `TicketOrderAppServiceImpl.placeOrderCAS` và `decreaseStockLevel3CAS`. Redis Lua atomic gate → DB safety net.

## Tệp Java nguồn (chỉ đọc)

- `xxxx-application/.../application/service/order/impl/TicketOrderAppServiceImpl.java:96-220` — `decreaseStockLevel3CAS`
- `xxxx-application/.../application/service/order/impl/TicketOrderAppServiceImpl.java:159-220` — `placeOrderCAS`
- `xxxx-application/.../application/service/order/cache/StockOrderCacheService.java` — Lua script
- `xxxx-domain/.../domain/service/TickerOrderDomainService.java`
- `xxxx-controller/.../controller/http/TicketOrderController.java`

## File .NET đích (sẽ tạo)

- `src/FlashSale.Infrastructure/Cache/StockOrderCacheService.cs` — Lua script (Redis EVAL)
- `src/FlashSale.Application/Services/Implementations/TicketOrderAppServiceImpl.cs` — full CAS logic
- `src/FlashSale.Infrastructure/Persistence/Repositories/TickerOrderRepositoryImpl.cs`
- `src/FlashSale.Infrastructure/Persistence/Repositories/TicketDetailRepositoryImpl.cs` — `TryDecreaseStockAsync`
- `src/FlashSale.Api/Controllers/TicketOrderController.cs` — endpoints

## Lua script (port from Java exactly)

```lua
-- KEYS[1] = "PRO_TICKET:{id}:stock_available"
-- ARGV[1] = quantity (decrement amount)
local stock = redis.call('GET', KEYS[1])
if stock == false then return -1 end
if tonumber(stock) < tonumber(ARGV[1]) then return 0 end
redis.call('DECRBY', KEYS[1], ARGV[1])
return 1
```

Use `IDatabase.ScriptEvaluateAsync(script, keys, values)`.

## placeOrderCAS flow

```
1. Lua atomic decrement → -1/0/1
   if -1 (cache miss) → warm from DB then retry
   if 0  (OOS)         → return PlaceOrderResponse.Failed("OUT_OF_STOCK", ...)
2. isRedisDecremented = true
3. DB: UPDATE ticket_item SET stock_available = stock_available - ? WHERE id = ? AND stock_available >= ?
   if 0 affected rows → compensate Redis (+quantity), return STOCK_CONFLICT
4. Read unitPrice from cache → if <= 0 compensate, return PRICE_NOT_FOUND
5. Build TickerOrder, insert into ticket_order_yyyyMM (Dapper)
6. Return PlaceOrderResponse.Ok(orderNumber)
```

## Endpoints to mirror

| Method | Route | Behaviour |
|--------|-------|-----------|
| GET | `/order/{ticketId}/{quantity}/order` | Level 1 (MySQL pessimistic) |
| GET | `/order/{ticketId}/{quantity}/cas` | Level 3 (Lua CAS) — returns bool |
| POST | `/order/cas` | JSON body, full CAS flow returning PlaceOrderResponse |

## Acceptance criteria

- [ ] Lua script loaded via SCRIPT LOAD + EVALSHA
- [ ] Cache miss path warms Redis from DB then retries
- [ ] DB rollback path compensates Redis
- [ ] Order number format matches Java: `"OKX-SGN-{userId}-{seq}-{tsMillis}"` (configurable prefix)
- [ ] `ThreadLocalRandom.nextInt(1, 10)` → `Random.Shared.Next(1, 10)` for demo userId
- [ ] 1000 concurrent requests test: stock never goes negative, total decrements ≤ initial
- [ ] Unit + integration + contract test
- [ ] Update KNOWN_DIFFERENCES.md with verdict on order_number prefix and random userId

## Verification

```powershell
dotnet build FlashSale.slnx
dotnet test tests/FlashSale.UnitTests --filter "FullyQualifiedName~Cas"
dotnet test tests/FlashSale.IntegrationTests --filter "FullyQualifiedName~Cas"

# Smoke
curl "http://localhost:5080/order/4/2/cas"
curl -X POST http://localhost:5080/order/cas -H "Content-Type: application/json" \
  -d '{"ticketId":4,"quantity":2}'
```

## Suggested commit

```
[TASK-013] order_cas_slice: preserve lua atomic gate and manual redis compensation
```