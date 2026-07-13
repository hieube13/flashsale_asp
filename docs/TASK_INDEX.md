# TASK_INDEX — Live task tracker

24-task migration plan (22 backend + 2 frontend). Each row is updated by the Agent at the end of every execution.

## How to use

- `Status`: `pending` → `in-progress` → `done`
- `Branch`: `f_task_XXX_slug`
- `Commit`: short hash, filled after `git commit`
- `Completed`: ISO date

## Live status

| ID | Title | Module | Status | Branch | Commit | Completed | Notes |
|----|-------|--------|--------|--------|--------|-----------|-------|
| TASK-001 | solution_scaffold | infra | done | — | — | 2026-07-13 | FlashSale.slnx + 5 src + 4 test projects, build green |
| TASK-002 | shared_contracts | contracts | done | — | — | 2026-07-13 | ResultMessage, PlaceOrderResponse, ResultCode, DTOs, PlaceOrderMqMessage |
| TASK-003 | docker_compose | infra | done | — | — | 2026-07-13 | MySQL 3316 + Redis 6319 + Kafka 9094 (KRaft) |
| TASK-004 | domain_entities | domain | done | — | — | 2026-07-13 | Ticket, TicketDetail, TickerOrder, OrderQueue, OutboxEvent, IdempotencyKey, PaymentTransaction, Booking + enums |
| TASK-005 | infrastructure_data | infra | done | — | — | 2026-07-13 | FlashSaleDbContext (8 entities), IDbConnectionFactory, MySqlConnectionFactory |
| TASK-006 | infrastructure_redis | infra | done | — | — | 2026-07-13 | IRedisInfrasService + RedisInfrasService (StackExchange.Redis), IStockOrderCacheService stub |
| TASK-007 | infrastructure_kafka | infra | done | — | — | 2026-07-13 | IKafkaOrderProducer + KafkaOrderProducer (Confluent.Kafka), KafkaOrderConsumerWorker stub |
| TASK-008 | application_scaffold | application | done | — | — | 2026-07-13 | 9 service interfaces (Ticket, Order, OrderMQ, Payment, Booking, Employee, Event) |
| TASK-009 | api_foundation | api | done | — | — | 2026-07-13 | Program.cs DI + Serilog + Prometheus /metrics + OutboxPublisherWorker stub |
| TASK-010 | observability | api | done | — | — | 2026-07-13 | prometheus-net, Serilog request logging, /metrics, /health + ArchitectureTests (5 NetArchTest rules) |
| TASK-011 | catalog_ticket_slice | catalog | done | `f_task_011_catalog_ticket_slice` | — | 2026-07-13 | Ticket + TicketDetail CRUD + L1/L2 cache + warmup worker — 8 unit tests pass, smoke parity OK |
| TASK-012 | order_read_slice | catalog | done | `f_task_012_order_read_slice` | — | 2026-07-14 | Dapper dynamic table for `ticket_order_{yyyyMM}` — `ITickerOrderRepository` + `TickerOrderRepositoryImpl` (`Infrastructure/Data/Dynamic/`) + `OrderDeductionDomainService` (`ExtractYearMonth` from order_number trailing ts) + `TicketOrderAppServiceImpl` (findAll/findPage/findByOrderNumber) + `OrderController` 3 routes. 10 new unit tests, all green. Stubs: TicketOrderAppServiceStub removed. |
| TASK-013 | order_cas_slice | order | done | `f_task_013_order_cas_slice` | — | 2026-07-14 | Redis Lua atomic decrement + MySQL FOR UPDATE safety net + Dapper insert into `ticket_order_{yyyyMM}`. `IStockOrderCacheService` moved from `Infrastructure/Cache/` → `Application/Services/` (abstraction belongs to higher layer). `StockOrderCacheService` now wraps the Lua script (`scripts/decrement_stock.lua` embedded) returning -1/0/1 semantics + handles NOSCRIPT re-eval. On-demand warmup reads from `ticket_item` table and primes both `stock_available` and `price_flash` keys. `TicketOrderAppServiceImpl` gains `PlaceOrderCasAsync`, `DecreaseStockLevel1Async`, `DecreaseStockLevel3CasAsync`, `GetStockAvailableAsync`. `OrderController` exposes `POST /order/cas`, `GET /order/{ticketId}/{quantity}/order|cas`, `GET /order/{ticketId}/{quantity}/{userId}/queued` (queued returns 501 — TASK-015). Order number format `OKX-SGN-{userId}-{seq}-{tsMillis}` and random `userId = Random.Shared.Next(1,10)` preserved (Java quirks). 10 new unit tests (6 PlaceOrderCas scenarios + 2 L3 + 1 GetStock + 1 invalid qty). 43/43 unit + 5/5 arch tests pass. |
| TASK-014 | order_cancel_slice | order | done | `f_task_014_order_cancel_slice` | — | 2026-07-14 | Distributed Redis lock (`LOCK:CANCEL_ORDER:{orderNumber}`, wait 1 s / expiry 5 s) + ownership check + idempotent status flip (already CANCELLED → true, no double restore) + DB stock restore via EF Core `IncreaseStockAsync` + best-effort Redis stock restore. `IDistributedLockProvider` / `IDistributedLock` moved from `Infrastructure/DistributedLock/` → `Application/Services/` to keep the abstraction visible to the Application layer (matches TASK-013's `IStockOrderCacheService` move). New `RedLockFactoryBuilder` (`Infrastructure/DistributedLock/`) wraps `RedLockNet.SERedis.RedLockFactory.Create(endpoints)` so the Api project never sees `RedLockEndPoint`. `RedLockDistributedLockProvider` now backed by real `RedLock.net` (was NoOp stub). `OrderController` exposes `PUT /order/{userId}/{orderNumber}/cancel`. HTTP 200 + `success=false` on lock-busy / not-found / ownership-mismatch / status-update-fail (matches Java semantics, `KNOWN_DIFFERENCES.md` §4). 9 new unit tests. 52/52 unit + 5/5 arch tests pass. |
| TASK-015 | order_mq_producer | order-mq | done | `f_task_015_order_mq_producer` | — | 2026-07-14 | `OrderMqAppServiceImpl.PlaceOrderMqAsync` mirrors Java `OrderMQAppServiceImpl.placeOrderMQ` (lines 38-104): Redis Lua pre-deduct (`DecreaseStockCacheByLuaAsync`) → cache-miss warm + retry → unit-price lookup → atomic transactional write via new `IOrderMqTransactionService` (Application abstraction, EF `IDbContextTransaction` impl in `Infrastructure/Persistence/OrderMqTransactionServiceImpl.cs`) → INSERT `order_queue` + INSERT `outbox_event` in 1 tx. On any throw → compensate Redis via `IncreaseStockCacheAsync` + return FAILED with `INTERNAL_ERROR: …`. Token format `MQ-` + first 16 hex chars of `Guid.NewGuid("N")` (matches Java line 63). Random userId 1-9 preserved (Java quirk — KNOWN_DIFFERENCES #7). EF Core repos: `OrderQueueRepositoryImpl` (CRUD + `UpdateStatusAsync` via `ExecuteUpdateAsync`), `OutboxEventRepositoryImpl` (PENDING-batch read + `MarkPublishedAsync/BatchAsync` for TASK-017). `OrderMQController` exposes `POST /order/mq` (request: `PlaceOrderMqRequest{ticketId, quantity}`) and `GET /order/mq/status/{token}` — both return HTTP 200 with `ResultMessage<T>` (success/failure in body, KNOWN_DIFFERENCES.md §4). DDL for `order_queue` + `outbox_event` added to `environment/mysql/init/01-schema.sql`. `OrderMqAppServiceStub` removed from `Stubs.cs`. 12 new unit tests. 64/64 unit + 5/5 arch tests pass. |
| TASK-016 | order_mq_consumer | order-mq | pending | — | — | — | Idempotency gate + DB decrement + insert order + update order_queue.status |
| TASK-017 | order_mq_publisher | order-mq | pending | — | — | — | SELECT ... FOR UPDATE SKIP LOCKED cron + Kafka ACK + mark PUBLISHED |
| TASK-018 | payment_vnpay | payment | pending | — | — | — | HMAC-SHA512 + URL builder + callback signature verify |
| TASK-019 | employee_timesheet | employee | pending | — | — | — | RedLock RBitSet monthly attendance bitmap |
| TASK-020 | booking_demo | booking | pending | — | — | — | BookingAppServiceImpl + HiController + SecureApiController + ticket/ping/java |
| TASK-021 | parity_tests | testing | pending | — | — | — | Golden JSON comparison Java vs .NET for 21 endpoints |
| TASK-022 | cutover | ops | pending | — | — | — | nginx shadow traffic → 50/50 → 100% with rollback plan |
| TASK-023 | frontend_migrate | frontend | pending | — | — | — | Port `xxxx.fe.com` (React 19 + Vite 8) → repo mới, đổi baseURL sang `.NET :5080`, add dev-proxy, docker-compose FE service |
| TASK-024 | frontend_smoke_e2e | frontend | pending | — | — | — | End-to-end smoke: FE ↔ Api ↔ MySQL/Redis/Kafka cho 10 user-facing endpoints |

## Done (history)

> Archived after each task is committed. The 10 scaffold rows above are kept inline
> for visibility during Phase 0. Once Phase 1 starts (TASK-011), done rows are
> relocated here and the live table is pruned back to pending/in-progress only.
> Phase 3 (TASK-023, TASK-024) covers frontend migration and is appended after the
> backend phases complete.

| ID | Title | Completed | Summary |
|----|-------|-----------|---------|
| TASK-001 | solution_scaffold | 2026-07-13 | `FlashSale.slnx` + 5 src + 4 test projects. Dependency graph: Api → Application → Infrastructure → Domain ← Contracts. |
| TASK-002 | shared_contracts | 2026-07-13 | `ResultMessage<T>`, `PlaceOrderResponse`, `ResultCode` enum, request/response DTOs, `PlaceOrderMqMessage`. Note: `ResultCode` lives in `FlashSale.Contracts.Dto` (not Domain) — kept here for shared use. |
| TASK-003 | docker_compose | 2026-07-13 | MySQL 3316, Redis 6319, Kafka 9094 (KRaft), Prometheus, Grafana. App port 5080. |
| TASK-004 | domain_entities | 2026-07-13 | 8 entities + 4 enums (OrderStatus, OrderQueueStatus, OutboxStatus). No external deps. |
| TASK-005 | infrastructure_data | 2026-07-13 | `FlashSaleDbContext` (EF Core), `IDbConnectionFactory` + `MySqlConnectionFactory` (Dapper). |
| TASK-006 | infrastructure_redis | 2026-07-13 | `IRedisInfrasService` + `RedisInfrasService` (StackExchange.Redis), `IStockOrderCacheService` stub (Lua in TASK-013). |
| TASK-007 | infrastructure_kafka | 2026-07-13 | `IKafkaOrderProducer` + `KafkaOrderProducer` (Confluent.Kafka), `KafkaOrderConsumerWorker` BackgroundService stub. |
| TASK-008 | application_scaffold | 2026-07-13 | 9 service interfaces in `FlashSale.Application.Services`. |
| TASK-009 | api_foundation | 2026-07-13 | `Program.cs` DI, Kestrel :5080, Serilog, Prometheus `/metrics`, `OutboxPublisherWorker` stub. |
| TASK-010 | observability | 2026-07-13 | Prometheus-net, Serilog request logging, `/health`, plus 5 NetArchTest dependency-direction tests enforcing the architecture graph. |
| TASK-011 | catalog_ticket_slice | 2026-07-13 | `TicketAppServiceImpl` + `TicketDetailAppServiceImpl` (Java parity) + `ITicketDomainService`/`TicketDomainServiceImpl` + EF Core `TicketRepositoryImpl` + `TicketDetailRepositoryImpl` (FOR UPDATE CAS) + `ITicketCacheService` + `ITicketDetailCacheService` (2-tier L1 Memory + L2 Redis) + `WarmupDataWorker` (BackgroundService, 5s startup delay, 24h cadence) + `TicketController` (7 endpoints) + `TicketDetailController` (3 endpoints incl. `/ticket/ping/java`) + `TicketMapper`. 8 unit tests pass (Moq TicketRepo + CacheService). Program.cs DI swaps `TicketAppServiceStub`/`TicketDetailAppServiceStub` → real impls, registers `IMemoryCache`. `TicketDto` extended with `PriceOriginal/PriceFlash/StockInitial/StockAvailable` (enriched from first TicketDetail — mirrors Java). `environment/mysql/init/01-schema.sql` for ticket + ticket_item tables. 12 smoke endpoints all return 200 + correct JSON. See also `KNOWN_DIFFERENCES.md` entry 2 (status default), 3 (Ping returns `{"status":"OK"}` not ResultMessage). |
| TASK-013 | order_cas_slice | 2026-07-14 | `IStockOrderCacheService` moved from `FlashSale.Infrastructure.Cache` → `FlashSale.Application.Services` (interface lives in higher layer per architecture rules). `StockOrderCacheService` impl swaps naive INCR/DECR for an embedded Lua atomic-decrement script with `-1/0/1` semantics + NOSCRIPT re-eval. `AddStockAvailableToCacheAsync` now reads `ticket_item` rows for the given activity and primes `PRO_TICKET:{id}:stock_available` + `PRO_TICKET:{id}:price_flash`. `GetEffectivePriceAsync` falls back to MySQL on cache miss and re-primes. `TicketOrderAppServiceImpl.PlaceOrderCasAsync` mirrors Java `placeOrderCAS` (lines 161-220): Redis-Lua → DB FOR UPDATE safety net → insert `TickerOrder` with random userId 1-9 + OKX-SGN-{userId}-{seq}-{tsMillis} format. `DecreaseStockLevel3CasAsync` mirrors Java `decreaseStockLevel3CAS` (lines 96-157). `DecreaseStockLevel1Async` = pure MySQL pessimistic (uses `ITicketDetailRepository.GetStockAvailableAsync` + `TryDecreaseStockAsync`). `GetStockAvailableAsync` = DB read. `OrderController` exposes 4 new routes: `POST /order/cas?ticketId=…&quantity=…`, `GET /order/{ticketId}/{quantity}/order`, `GET /order/{ticketId}/{quantity}/cas`, `GET /order/{ticketId}/{quantity}/{userId}/queued` (TASK-015 stub returns 501). All wrap responses in `ResultMessage<T>`. Tests: 10 new (43/43 unit + 5/5 arch green). Quirks preserved: random userId (`Random.Shared.Next(1,10)`), `Order -> Pending` literal note, `OKX-SGN` terminal_id, 200 + `success:false` body on error. See `KNOWN_DIFFERENCES.md` §3 (random userId — preserved), §8 (outbox userId — preserved). |
| TASK-014 | order_cancel_slice | 2026-07-14 | Distributed Redis lock via real `RedLock.net` (`RedLockDistributedLockProvider` + `RedLockFactoryBuilder`); `IDistributedLockProvider`/`IDistributedLock` abstractions moved from `Infrastructure/DistributedLock/` → `Application/Services/` (mirrors TASK-013 move of `IStockOrderCacheService`). `TicketOrderAppServiceImpl.CancelOrderAsync` mirrors Java `cancelOrder` (lines 439-511): acquire lock → resolve year-month shard via `ExtractYearMonth` → fetch order → ownership check → idempotent if already CANCELLED → `UpdateStatusAsync(2)` → DB `IncreaseStockAsync` → best-effort Redis `IncreaseStockCacheAsync`. `OrderController` exposes `PUT /order/{userId}/{orderNumber}/cancel` (mirrors Java `TicketOrderController.cancelOrder`). All 8 branches return HTTP 200 + `success:true|false` (matches Java + `KNOWN_DIFFERENCES.md` §4). Tests: 9 new — happy path, lock-busy, order-not-found, ownership mismatch, already-cancelled, status-update fail, redis-restore fail, empty orderNumber, lock key naming. 52/52 unit + 5/5 arch green. |
| TASK-015 | order_mq_producer | 2026-07-14 | `OrderMqAppServiceImpl.PlaceOrderMqAsync` mirrors Java `OrderMQAppServiceImpl.placeOrderMQ` (lines 38-104): Redis Lua pre-deduct (`DecreaseStockCacheByLuaAsync`) → cache-miss warm + retry → unit-price lookup → atomic transactional write via new `IOrderMqTransactionService` (Application abstraction, EF `IDbContextTransaction` impl in `Infrastructure/Persistence/OrderMqTransactionServiceImpl.cs`) → INSERT `order_queue` + INSERT `outbox_event` in 1 tx. On any throw → compensate Redis via `IncreaseStockCacheAsync` + return FAILED with `INTERNAL_ERROR: …`. Token format `MQ-` + first 16 hex chars of `Guid.NewGuid("N")` (matches Java line 63). Random userId 1-9 preserved (Java quirk — KNOWN_DIFFERENCES #7). EF Core repos: `OrderQueueRepositoryImpl` (CRUD + `UpdateStatusAsync` via `ExecuteUpdateAsync`), `OutboxEventRepositoryImpl` (PENDING-batch read + `MarkPublishedAsync/BatchAsync` for TASK-017). `OrderMQController` exposes `POST /order/mq` (request: `PlaceOrderMqRequest{ticketId, quantity}`) and `GET /order/mq/status/{token}` — both return HTTP 200 with `ResultMessage<T>` (success/failure in body, KNOWN_DIFFERENCES.md §4). DDL for `order_queue` + `outbox_event` added to `environment/mysql/init/01-schema.sql`. `OrderMqAppServiceStub` removed from `Stubs.cs`. 12 new unit tests. 64/64 unit + 5/5 arch tests pass. |

## Definition of Done (per task)

```
[ ] dotnet build FlashSale.slnx                    0 error
[ ] dotnet test tests/FlashSale.UnitTests          green
[ ] dotnet test tests/FlashSale.ArchitectureTests  green
[ ] No hard-coded secrets / connection strings
[ ] Workers: idempotent, explicit Ack/Nack
[ ] Message chain: CorrelationId propagated
[ ] TASK_INDEX.md: status=done, branch, commit
[ ] FLASH_SALE_ARCHITECTURE.md updated (if API/DB changed)
[ ] INTERNAL_ARCHITECTURE.md updated (if entity/worker added)
[ ] KNOWN_DIFFERENCES.md updated (if behaviour diverges)
[ ] Commit: [TASK-XXX] slug: mô tả ngắn
[ ] Suggested commit printed — NEVER auto-commit
```

Per-task checklist (paste into PR description or commit body):

```
[TASK-XXX] YYYY-MM-DD
[x] build        0 error / 0 warning
[x] unit tests   N/N pass
[x] arch tests   green
[x] secrets      none hard-coded
[x] workers      idempotent + ack
[x] correlation  propagated end-to-end
[x] index        TASK_INDEX.md row updated
[x] arch doc     FLASH_SALE_ARCHITECTURE.md §10 endpoints table (if changed)
[x] internal     INTERNAL_ARCHITECTURE.md §11 status (if changed)
[x] diffs        KNOWN_DIFFERENCES.md entry added (if any)
```

## How to update

At the end of each task, Agent must:

1. Mark `[x]` in this row's checklist.
2. Update `Status`, `Branch`, `Commit`, `Completed`.
3. Commit `docs/TASK_INDEX.md` separately with message `docs: update TASK_INDEX [TASK-XXX]`.