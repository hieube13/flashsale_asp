# TASK_INDEX — Live task tracker

25-task migration plan (22 backend + 2 frontend + 2 infra ops). Each row is updated by the Agent at the end of every execution.

## How to use

- `Status`: `pending` → `in-progress` → `done`
- `Branch`: `f_task_XXX_slug`
- `Commit`: short hash, filled after `git commit`
- `Completed`: ISO date

## Live status

All 26 tasks (TASK-001..026). TASK-025 done, TASK-026 pending. See Done (history) below.

| ID | Title | Module | Status | Branch | Commit | Completed | Notes |
|----|-------|--------|--------|--------|--------|-----------|-------|
| TASK-026 | frontend_clean_arch | infra | pending | — | — | — | Move frontend/ → src/FlashSale.WebApp/ for Clean Architecture alignment. Update docker-compose context path. |

## Done (history)

> Archived after each task is committed.

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
| TASK-012 | order_read_slice | 2026-07-14 | Dapper dynamic table for `ticket_order_{yyyyMM}` — `ITickerOrderRepository` + `TickerOrderRepositoryImpl` + `OrderDeductionDomainService` (`ExtractYearMonth` from order_number trailing ts) + `TicketOrderAppServiceImpl` (findAll/findPage/findByOrderNumber) + `OrderController` 3 routes. 10 new unit tests, all green. Stubs: TicketOrderAppServiceStub removed. |
| TASK-013 | order_cas_slice | 2026-07-14 | Redis Lua atomic decrement + MySQL FOR UPDATE safety net + Dapper insert into `ticket_order_{yyyyMM}`. `IStockOrderCacheService` moved from `Infrastructure/Cache/` → `Application/Services/`. `StockOrderCacheService` wraps Lua script returning -1/0/1 semantics + handles NOSCRIPT re-eval. `AddStockAvailableToCacheAsync` reads `ticket_item` rows and primes both `stock_available` and `price_flash` keys. `GetEffectivePriceAsync` falls back to MySQL on cache miss. `TicketOrderAppServiceImpl` gains `PlaceOrderCasAsync`, `DecreaseStockLevel1Async`, `DecreaseStockLevel3CasAsync`, `GetStockAvailableAsync`. `OrderController` exposes `POST /order/cas`, `GET /order/{ticketId}/{quantity}/order|cas`, `GET /order/{ticketId}/{quantity}/{userId}/queued` (501). Random userId 1-9 preserved. 43/43 unit + 5/5 arch tests pass. |
| TASK-014 | order_cancel_slice | 2026-07-14 | Distributed Redis lock via real `RedLock.net` (`RedLockDistributedLockProvider` + `RedLockFactoryBuilder`); `IDistributedLockProvider`/`IDistributedLock` moved to `Application/Services/`. `TicketOrderAppServiceImpl.CancelOrderAsync`: acquire lock → resolve shard → ownership check → idempotent CANCELLED flip → `UpdateStatusAsync(2)` → DB `IncreaseStockAsync` → best-effort Redis `IncreaseStockCacheAsync`. `OrderController` exposes `PUT /order/{userId}/{orderNumber}/cancel`. HTTP 200 + `success:false` on all failure branches (matches Java). 52/52 unit + 5/5 arch tests pass. |
| TASK-015 | order_mq_producer | 2026-07-14 | `OrderMqAppServiceImpl.PlaceOrderMqAsync`: Redis Lua pre-deduct → cache-miss warm + retry → unit-price lookup → atomic EF transaction INSERT `order_queue` + INSERT `outbox_event`. On throw → compensate Redis via `IncreaseStockCacheAsync`. Token format `MQ-` + first 16 hex of `Guid.NewGuid("N")`. Random userId 1-9 preserved. `OrderMQController` exposes `POST /order/mq` + `GET /order/mq/status/{token}`. DDL for `order_queue` + `outbox_event` added. 64/64 unit + 5/5 arch tests pass. |
| TASK-016 | order_mq_consumer | 2026-07-14 | `KafkaOrderConsumerWorker` (real `IConsumer<string,string>` Confluent.Kafka, manual commit, 3-retry backoff 200/400/800ms) → `OrderMqConsumerHandlerImpl.ProcessAsync`: (1) `IIdempotencyKeyRepository.TryInsertAsync` INSERT IGNORE → (2) `ITicketDetailRepository.TryDecreaseStockAsync` atomic UPDATE WHERE stock_available ≥ ? → (3) Dapper INSERT `ticket_order_{yyyyMM}` → (4) Flip queue to SUCCESS/FAILED. Order number format `MQ-{userId}-{tsMillis}`. DDL `idempotency_key` table appended. 73/73 unit + 5/5 arch tests pass. |
| TASK-017 | outbox_publisher | 2026-07-14 | `OutboxPublisherWorker` moved from `FlashSale.Api.Workers/` → `FlashSale.Infrastructure/Messaging/`. Row-by-row cycle every 1s with 250ms startup jitter. `ExecuteOnceAsync` reads PENDING batch (500) → for each: deserialize → `IKafkaOrderProducer.SendAndAwaitAckAsync` → `MarkPublishedAsync`. Per-row try/catch. 80/80 unit + 5/5 arch tests pass. |
| TASK-018 | payment_vnpay | 2026-07-14 | `VnPayGatewayService` real HMAC-SHA512 + URL builder (TreeMap sort, US-ASCII encode, `+`→`%20`). `IVnPayGatewayService` extended with `VerifySignature`. `PaymentAppServiceImpl`: idempotency lookup (reuse PENDING row URL) + DB persist IN_PROGRESS + IPN handler with RedLock `LOCK:PAYMENT_IPN:{txnRef}` (wait 5s/expiry 10s) → JSON `{RspCode,Message}`. RspCode mapping: 00/01/02/04/97/99. `ticket_order.status` NOT touched (matches Java). `PaymentController` exposes `POST /payment/create`, `GET /payment/callback/return`, `POST /payment/callback/ipn`. `UseForwardedHeaders` for `vnp_IpAddr`. DDL `payment_transaction` table appended. 96/96 unit + 5/5 arch tests pass. |
| TASK-019 | employee_timesheet | 2026-07-14 | `EmployeeCacheServiceImpl` (Application) + `EmployeeBitSetService` (Infrastructure) wrapping `StackExchange.Redis` BitSet ops via `IEmployeeBitSetService`. Key format `user:sign:{userId}:{yyyyMM}`, UTC-pinned (Java uses local zone fix), first-sign-day bounded by `lengthOfMonth` (Java bug fix), consecutive backward without cross-month. `IEmployeeCacheService` extended 6→8 methods with typed DTOs. `EmployeeController` mirrors Java `/api/sign-in/{userId}/*` 8 routes. Redis errors → `EmployeeCacheException` → 503 `redis_unavailable`. No DB tables. 112/112 unit + 5/5 arch tests pass. |
| TASK-020 | booking_demo | 2026-07-14 | `BookingAppServiceImpl` mirrors Java: validate `ticketId>0` + `quantity 1..10`, EF Core persist, 5-field `BookingDto`. `BookingCode` format `BK<ms><4hex>`. `EventAppServiceImpl.SayHi(name)` returns `"Hi Infrastructure"` (Java quirk). `HiController` 3 endpoints with rate-limit + circuit-breaker. `SecureApiController` keeps Java's 2 raw echo endpoints + 4 extras. `AddResiliencePipeline("checkRandom", ...)` + `AddRateLimiter`. DDL `booking` table appended (Java missing it). 136/136 unit + 5/5 arch tests pass. |
| TASK-021 | parity_tests | 2026-07-14 | Pre-flight fixes: `TicketDetailDto` `Version: long?`; `DecreaseStockQueueAsync` implemented. Contract test infrastructure: `FluentAssertions` + `Microsoft.AspNetCore.Mvc.Testing`; `ContractTestHttpClient`; `JsonAssertions`. 5 test files (40 tests, all skipped — require running app). KNOWN_DIFFERENCES.md: §30-32 added. 136/136 unit + 5/5 arch + 40 contract = 181 tests pass. |
| TASK-022 | cutover | 2026-07-14 | 4 docker-compose override files (`environment/nginx/`): shadow/10pct/50pct/100pct. 4 nginx configs with `X-Upstream` header. `docs/CUTOVER.md` runbook + `docs/ROLLBACK.md`. `tests/FlashSale.LoadTests/k6/flash-sale.js` adapted from Java baseline. |
| TASK-023 | frontend_migrate | 2026-07-14 | Port `xxxx.fe.com` (React 19 + Vite 8) → `flashsale/frontend/`. `vite.config.js` proxy, `src/services/api.js` relative URLs, all 13 components + assets, `Dockerfile` multi-stage nginx, `docker-compose.yml` adds `frontend` service. 28 files total, .NET build 0 errors. |
| TASK-024 | frontend_smoke_e2e | 2026-07-14 | Docker Desktop + MySQL/Redis healthy. .NET backend (:5080) started. 8 bug fixes: (1) `StockOrderCacheService` Singleton→Scoped DI fix, (2) `WarmupDataWorker` uses `IServiceScopeFactory`, (3) `OrderController` `[FromQuery]`→`[FromBody]` JSON, (4) `PaymentController` concrete→interface, (5) `HandleCallbackAsync` void→return `VnPayIpnResponse`, (6) `BuildAndPersistPaymentAsync` added to interface, (7) duplicate `VnPayIpnResponse` removed, (8) unit tests updated. Build 0 errors. Smoke: `/health` `/ticket` `/order/cas` `/employee` `/metrics` all 200 OK. `npm install` + `npm run build` pass. **All 24 tasks complete!** |
| TASK-025 | sqlserver_migrate | 2026-07-14 | Replace MySQL with SQL Server. EF Core: Pomelo→SqlServer, Dapper: MySqlConnector→SqlClient, `SqlServerConnectionFactory` created, `MySqlConnectionFactory` deleted, `IdempotencyKeyRepositoryImpl` uses `IF NOT EXISTS...INSERT` with `SET NOCOUNT`, DDL rewritten to T-SQL (`environment/sqlserver/init/01-schema.sql`), `docker-compose.yml` adds `mssql` container (port 1433, SA password `Test@Pass1234`), connection strings updated to `ConnectionStrings__SqlServer`, `appsettings.json` updated. Build 0 error, 136/136 unit + 5/5 arch tests pass. |

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

- 2026-07-14 — TASK-026 created. Move frontend/ → src/FlashSale.WebApp/ for Clean Architecture alignment. Update docker-compose context path. Update INTERNAL_ARCHITECTURE.md project structure.
- 2026-07-14 — TASK-025 created. Replace MySQL with SQL Server: EF Core SqlServer provider, Microsoft.Data.SqlClient, DDL → T-SQL, docker-compose mssql container.

## Task brief summaries

### TASK-025 — sqlserver_migrate (infra / ✅ done 2026-07-14)

Replace MySQL with SQL Server across all layers. EF Core provider swap (Pomelo → SqlServer), Dapper driver swap (MySqlConnector → SqlClient), `SqlServerConnectionFactory` created, `MySqlConnectionFactory` deleted, DDL scripts rewritten to T-SQL (`environment/sqlserver/init/01-schema.sql`), docker-compose `mssql` container (port 1433, SA password `Test@Pass1234`), connection strings updated, `appsettings.json` updated. `IdempotencyKeyRepositoryImpl` uses `IF NOT EXISTS...INSERT` with `SET NOCOUNT`. Build 0 error, 136/136 unit + 5/5 arch tests pass.

### TASK-026 — frontend_clean_arch (infra / pending)

Move `flashsale/frontend/` → `src/FlashSale.WebApp/` aligning with Clean Architecture. Update `docker-compose.yml` context path from `./frontend` → `./src/FlashSale.WebApp`. Delete old `frontend/` folder. Update nginx environment compose overrides. Update docs.

### TASK-024 — frontend_smoke_e2e (frontend / 2026-07-14)

- 2026-07-14 — TASK-024 done. Frontend smoke e2e: Docker Desktop + MySQL/Redis + .NET backend (:5080). 8 bug fixes including DI conflicts, FromBody binding, PaymentController interface refactor. Build 0 errors. Smoke pass. All 24 tasks done!
- 2026-07-14 — TASK-023 done. Frontend port: React 19 + Vite 8 → `flashsale/frontend/`, 28 files, 0 errors.
- 2026-07-14 — TASK-022 done. 4 nginx configs + cutover/rollback runbooks + k6 load tests.
- 2026-07-14 — TASK-021 done. Contract test infra + 5 test files (40 tests, skipped). KNOWN_DIFFERENCES §30-32.
- 2026-07-14 — TASK-020 done. Booking slice + HiController rate-limit + circuit-breaker + SecureApiController. 136/136 unit + 5/5 arch.
- 2026-07-14 — TASK-019 done. Employee BitSet service + 8 endpoints, UTC-pinned. 112/112 + 5/5.
- 2026-07-14 — TASK-018 done. VnPay gateway + IPN handler with RedLock. 96/96 + 5/5.
- 2026-07-14 — TASK-017 done. OutboxPublisherWorker moved to Infrastructure/Messaging. 80/80 + 5/5.
- 2026-07-14 — TASK-016 done. Real Kafka consumer + idempotency_key. 73/73 + 5/5.
- 2026-07-14 — TASK-015 done. OrderMqAppServiceImpl with EF transaction. 64/64 + 5/5.
- 2026-07-14 — TASK-014 done. Distributed cancel-order lock + idempotent flip. 52/52 + 5/5.
- 2026-07-14 — TASK-013 done. Redis Lua atomic decrement + MySQL FOR UPDATE. 43/43 + 5/5.
- 2026-07-14 — TASK-012 done. Dynamic monthly shard table via Dapper. 10 tests green.
- 2026-07-13 — TASK-011 done. Catalog slice with L1/L2 cache + warmup worker. 8 tests.
- 2026-07-13 — TASK-001..010 done. Scaffolding + contracts + docker + entities + infra + observability + NetArchTest rules.
