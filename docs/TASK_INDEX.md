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
| TASK-011 | catalog_ticket_slice | catalog | pending | — | — | — | Implement TicketAppServiceImpl (Java TicketAppServiceImpl) |
| TASK-012 | order_read_slice | catalog | pending | — | — | — | Dapper dynamic table for ticket_order_yyyyMM (list / paged / findByOrderNumber) |
| TASK-013 | order_cas_slice | order | pending | — | — | — | Redis Lua atomic decrement + DB safety net (decreaseStockLevel3CAS / placeOrderCAS) |
| TASK-014 | order_cancel_slice | order | pending | — | — | — | Distributed lock + DB status update + Redis restore |
| TASK-015 | order_mq_producer | order-mq | pending | — | — | — | Lua pre-deduct + insert order_queue + outbox_event in 1 tx |
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

## Definition of Done (per task)

```
[x] dotnet build FlashSale.slnx                    0 error
[x] dotnet test tests/FlashSale.UnitTests          green
[x] dotnet test tests/FlashSale.ArchitectureTests  green
[x] No hard-coded secrets / connection strings
[x] Workers: idempotent, explicit Ack/Nack
[x] Message chain: CorrelationId propagated
[x] TASK_INDEX.md: status=done, branch, commit
[x] FLASH_SALE_ARCHITECTURE.md updated (if API/DB changed)
[x] INTERNAL_ARCHITECTURE.md updated (if entity/worker added)
[x] KNOWN_DIFFERENCES.md updated (if behaviour diverges)
[x] Commit: [TASK-XXX] slug: mô tả ngắn
[x] Suggested commit printed — NEVER auto-commit
```

## How to update

At the end of each task, Agent must:

1. Mark `[x]` in this row's checklist.
2. Update `Status`, `Branch`, `Commit`, `Completed`.
3. Commit `docs/TASK_INDEX.md` separately with message `docs: update TASK_INDEX [TASK-XXX]`.