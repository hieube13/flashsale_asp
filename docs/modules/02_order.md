# Module 02 — Order (CAS + Cancel)

## Java → .NET

| Java | .NET |
|------|------|
| `com.xxxx.ddd.controller.http.TicketOrderController` | `FlashSale.Api.Controllers.TicketOrderController` |
| `com.xxxx.ddd.application.service.order.TicketOrderAppService` | `FlashSale.Application.Services.ITicketOrderAppService` |
| `com.xxxx.ddd.application.service.order.impl.TicketOrderAppServiceImpl` | `FlashSale.Application.Services.Implementations.TicketOrderAppServiceImpl` |
| `com.xxxx.ddd.application.service.order.cache.StockOrderCacheService` | `FlashSale.Infrastructure.Cache.StockOrderCacheService` |
| `com.xxxx.ddd.domain.service.TickerOrderDomainService` | `FlashSale.Domain.Services.TickerOrderDomainService` (interface only; impl in Application) |
| `com.xxxx.ddd.domain.service.OrderDeductionDomainService` | `FlashSale.Domain.Services.IOrderDeductionDomainService` |

## Endpoints

| Method | Route | Behaviour |
|--------|-------|-----------|
| GET | `/order/{ticketId}/{quantity}/order` | Level 1 — MySQL pessimistic `SELECT ... FOR UPDATE` |
| GET | `/order/{ticketId}/{quantity}/cas` | Level 3 — Lua atomic decrement + DB safety net |
| POST | `/order/cas` | Full CAS, JSON body `{ticketId, quantity}`, returns `PlaceOrderResponse` |
| GET | `/order/{ticketId}/{quantity}/{userId}/queued` | MQ path with distributed lock |
| PUT | `/order/{userId}/{orderNumber}/cancel` | Lock + DB status update + Redis restore |

## Algorithms

### placeOrderCAS (Lua + DB safety net)

```
1. Lua EVAL on PRO_TICKET:{id}:stock_available
   return -1 if cache miss, 0 if OOS, 1 if success
2. if cache miss → warm from DB → retry Lua
3. if OOS → return PlaceOrderResponse.Failed("OUT_OF_STOCK")
4. DB UPDATE ticket_item SET stock_available = stock_available - ? WHERE id = ? AND stock_available >= ?
5. if 0 affected rows → compensate Redis (+quantity), return STOCK_CONFLICT
6. read unitPrice from PRO_TICKET:{id}:price_flash; if missing compensate, PRICE_NOT_FOUND
7. INSERT ticket_order_{yyyyMM} (Dapper)
8. return PlaceOrderResponse.Ok(orderNumber)
```

### cancelOrder (lock + status + restore)

```
1. Acquire RedLock on "LOCK:CANCEL_ORDER:{orderNumber}" (wait=1s, lease=5s)
2. Extract yearMonth from orderNumber
3. Lookup order by orderNumber → owner check
4. UPDATE ticket_order_{yyyyMM} SET order_status=2 WHERE order_number=?
5. INCR PRO_TICKET:{id}:stock_available by quantity (best-effort)
6. Release lock in finally
```

## Cache keys

- `PRO_TICKET:{id}:stock_available` — atomic Lua decremented stock
- `PRO_TICKET:{id}:price_flash` — active sale price
- `LOCK:CANCEL_ORDER:{orderNumber}` — cancel mutex
- `TOKEN_LOCK_KEY{ticketId}` — MQ producer mutex

## Order number format

`OKX-SGN-{userId}-{seq}-{tsMillis}`

- prefix `OKX-SGN` from Java (configurable in .NET via `Orders:NumberPrefix`)
- `userId` from `ThreadLocalRandom.current().nextInt(1, 10)` (demo)
- `seq` from `AtomicLong` counter
- `tsMillis` from `System.currentTimeMillis()`

## Tasks

- **TASK-013**: order_cas_slice — port CAS Lua + DB safety net
- **TASK-014**: order_cancel_slice — port cancel with lock + restore

## Known quirks (see KNOWN_DIFFERENCES.md)

- HTTP 200 on error (Java returns success:false body, not 4xx)
- Random userId in `placeOrderCAS` (demo controller)
- order_number prefix hardcoded in Java (configurable in .NET)