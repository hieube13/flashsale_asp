# Module 01 — Catalog (Ticket + TicketDetail + Order Read)

## Java → .NET

| Java | .NET |
|------|------|
| `com.xxxx.ddd.controller.http.TicketController` | `FlashSale.Api.Controllers.TicketController` |
| `com.xxxx.ddd.controller.http.TicketDetailController` | `FlashSale.Api.Controllers.TicketDetailController` |
| `com.xxxx.ddd.application.service.ticket.TicketAppService` | `FlashSale.Application.Services.ITicketAppService` |
| `com.xxxx.ddd.application.service.ticket.TicketDetailAppService` | `FlashSale.Application.Services.ITicketDetailAppService` |
| `com.xxxx.ddd.application.service.ticket.impl.TicketAppServiceImpl` | `FlashSale.Application.Services.Implementations.TicketAppServiceImpl` |
| `com.xxxx.ddd.application.service.ticket.impl.TicketDetailAppServiceImpl` | `FlashSale.Application.Services.Implementations.TicketDetailAppServiceImpl` |
| `com.xxxx.ddd.application.service.ticket.cache.TicketDetailCacheService` | `FlashSale.Infrastructure.Cache.TicketDetailCacheService` |
| `com.xxxx.ddd.application.cronjob.WarmupDataBeforeEvent` | `FlashSale.Api.Workers.WarmupDataWorker` |
| `com.xxxx.ddd.application.service.order.TicketOrderAppService` (read side) | `FlashSale.Application.Services.ITicketOrderAppService` (findAll/findPage/findByOrderNumber) |

## Entities

- `Ticket` → `ticket` table
- `TicketDetail` → `ticket_item` table
- `TickerOrder` → `ticket_order_{yyyyMM}` (Dapper, dynamic)

## Endpoints

| Method | Route | Behaviour |
|--------|-------|-----------|
| GET | `/ticket/active` | All status=1 |
| POST | `/ticket/create` | Insert ticket + ticket_item in 1 tx |
| GET | `/ticket/{id}` | Detail |
| PUT | `/ticket/{id}` | Update |
| PUT | `/ticket/{id}/active` | Set status=1 |
| PUT | `/ticket/{id}/inactive` | Set status=0 |
| DELETE | `/ticket/{id}` | Soft delete status=2 |
| GET | `/ticket/{ticketId}/detail/{detailId}?version=` | Detail (with version for optimistic lock) |
| GET | `/ticket/{ticketId}/detail/{detailId}/order` | Decrement by 1 |
| GET | `/ticket/ping/java` | Sleep 1s, return OK |
| GET | `/order/{userId}/list?ntable=yyyyMM` | All orders in month |
| GET | `/order/{userId}/list/page?ntable=yyyyMM&cursor=lastId&limit=50` | Paged |
| GET | `/order/{userId}/{orderNumber}` | Single lookup (table from orderNumber) |

## Cache keys

- `PRO_TICKET:{id}:*` — active ticket snapshots
- `TICKET:{id}:*` — legacy cache key

## Tasks

- **TASK-011**: catalog_ticket_slice — port TicketController + TicketDetailController + cache + warmup
- **TASK-012**: order_read_slice — port read methods (findAll, findPage, findByOrderNumber) using Dapper

## Known quirks

- Java returns `ResultUtil.error(500, ...)` for validation errors on TicketController (should be 400 but isn't). **Verdict**: preserve.
- `/ticket/{id}` update is currently a no-op in Java (`return ResultUtil.data(null)`). **Verdict**: preserve.
- Java `/ticket/{ticketId}/detail/{detailId}` accepts optional `version` query param but never uses it. **Verdict**: keep parameter to preserve API surface, ignore internally.