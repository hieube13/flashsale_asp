# FLASH_SALE_ARCHITECTURE — System overview

Mirror of `xxxx.com-18-06-26` (Spring Boot 3.3.5 / Java 21 / DDD modular monolith / port 1122) re-implemented on ASP.NET Core 8 LTS (port 5080).

## 1. Goals

| Goal | Description |
|------|-------------|
| **Parity** | Every HTTP route, JSON property, Redis key, Kafka topic, DB schema is byte-identical to Java. |
| **Gradual cutover** | Java keeps running on 1122; .NET runs on 5080. nginx routes traffic. Switch is one config flag. |
| **Single source of truth** | Java repo is read-only during migration. All new code lives in this repo. |
| **Behaviour oracle** | When in doubt, the Java source decides. We diff golden JSONs (TASK-021). |

## 2. High-level diagram

```
                              ┌─────────────────────────────┐
                              │       nginx (port 80)       │
                              │ /ticket/* → Java:1122       │
                              │ /order/*  → Java:1122       │
                              │ /api/*    → Java:1122       │
                              │ /payment/*→ Java:1122       │
                              │ /hello/*  → Java:1122       │
                              │                             │
                              │ (TASK-022: switch per-route │
                              │  from Java → .NET:5080)     │
                              └──────────┬──────────────────┘
                                         │
              ┌──────────────────────────┼──────────────────────────┐
              │                          │                          │
              ▼                          ▼                          ▼
   ┌────────────────────┐     ┌────────────────────┐     ┌────────────────────┐
   │ xxxx.com (Java)    │     │ flashsale (.NET 8) │     │ Kafka              │
   │ port 1122          │     │ port 5080          │     │ topic order-place- │
   │ Spring Boot 3.3.5  │     │ ASP.NET Core 8     │     │ topic              │
   └──────────┬─────────┘     └──────────┬─────────┘     └────────────────────┘
              │                          │
              │                          │
              ▼                          ▼
       ┌──────────────┐         ┌──────────────┐
       │ MySQL        │         │ MySQL        │
       │ :3316 db=veta│         │ :3316 (same) │
       │ ticket_init. │         │ schema from  │
       │ sql          │         │ same DDL     │
       └──────────────┘         └──────────────┘
              │                          │
              └──────────┬───────────────┘
                         ▼
                 ┌────────────────┐
                 │ Redis :6319    │
                 │ (PRO_TICKET:*  │
                 │  TICKET:*      │
                 │  LOCK:*        │
                 │  user:sign:*)  │
                 └────────────────┘
```

## 3. Modules (mirrored 1-1 from Java)

| Module | Java package | .NET namespace | Status |
|--------|--------------|----------------|--------|
| catalog | `controller.http.TicketController`, `TicketDetailController` | `FlashSale.Api.Controllers.Ticket*` | 🟡 TASK-011 |
| catalog (read) | `TicketOrderAppServiceImpl.findAll/findPage/findByOrderNumber` | `FlashSale.Application.Services.TicketOrderAppServiceImpl` | ✅ TASK-012 |
| order | `TicketOrderAppServiceImpl.placeOrderCAS` | same | ✅ TASK-013 |
| order | `cancelOrder` | same | ✅ TASK-014 |
| order-mq | `OrderMQAppServiceImpl.placeOrderMQ` | `FlashSale.Application.Services.OrderMqAppService` | 🟡 TASK-015 |
| order-mq | `KafkaOrderConsumer` | `FlashSale.Api.Workers.KafkaOrderConsumerWorker` | 🟡 TASK-016 |
| order-mq | `OutboxPublisherJob` | `FlashSale.Api.Workers.OutboxPublisherWorker` | 🟡 TASK-017 |
| payment | `PaymentController`, `PaymentAppServiceImpl`, `VnPayGatewayServiceImpl` | `FlashSale.Api.Controllers.PaymentController`, `FlashSale.Application.Services.PaymentAppService`, `FlashSale.Infrastructure.External.VnPayGatewayService` | 🟡 TASK-018 |
| employee | `EmployeeController`, `EmployeeCacheService` | `FlashSale.Api.Controllers.EmployeeController`, `FlashSale.Application.Services.EmployeeCacheService` | 🟡 TASK-019 |
| booking | `BookingController`, `BookingAppService` | `FlashSale.Api.Controllers.BookingController`, `FlashSale.Application.Services.BookingAppService` | 🟡 TASK-020 |
| demo | `HiController`, `SecureApiController` | `FlashSale.Api.Controllers.*` | 🟡 TASK-020 |

## 4. Database schema

DDL is owned by `environment/mysql/init/ticket_init.sql` (mirrored from Java). EF Core's `OnModelCreating` declares the same shape for query side but never migrates the DB.

Tables:

| Table | Purpose | Mapping |
|-------|---------|---------|
| `ticket` | Event | `Ticket` entity |
| `ticket_item` | Event tier (VIP/Standard/etc) | `TicketDetail` entity |
| `ticket_order_{yyyyMM}` | Monthly shard for orders | Dapper dynamic table (`TickerOrderRepositoryImpl`) |
| `ticket_order` | Logical parent (TASK-012) — schema only, app writes to shards | — |
| `order_queue` | Async order tracking | `OrderQueue` entity |
| `outbox_event` | Transactional outbox | `OutboxEvent` entity |
| `idempotency_key` | Consumer dedupe | `IdempotencyKey` entity |
| `payment_transaction` | Payment records | `PaymentTransaction` entity |
| `booking` | Demo bookings | `Booking` entity |

## 5. Caching strategy (Redis)

Three cache tiers, all keyed identically to Java:

| Tier | Key prefix | Purpose |
|------|------------|---------|
| L1 (entity) | `PRO_TICKET:{id}:*` | Full ticket_detail snapshot for fast detail endpoint |
| L2 (stock) | `PRO_TICKET:{id}:stock_available` | Atomic Lua decremented stock counter |
| L2 (price) | `PRO_TICKET:{id}:price_flash` | Active price during sale window |
| Lock | `LOCK:CANCEL_ORDER:{orderNumber}` | Distributed lock for cancel |
| Lock | `TOKEN_LOCK_KEY{ticketId}` | MQ producer mutex |
| BitSet | `user:sign:{userId}:{yyyyMM}` | Monthly attendance bitmap |

## 6. Messaging (Kafka)

| Topic | Producer | Consumer | Group |
|-------|----------|----------|-------|
| `order-place-topic` | `placeOrderMQ` controller (after outbox) | `KafkaOrderConsumer` (concurrency=10) | `order-consumer-group` |

Outbox flow:
```
HTTP POST /order/mq
  → Lua atomic pre-deduct Redis
  → DB tx: INSERT order_queue + INSERT outbox_event
  → ACK 200 to user
OutboxPublisherWorker (1s fixedDelay):
  → SELECT FOR UPDATE SKIP LOCKED on outbox_event WHERE status=0
  → Kafka producer.sendAndAwaitAck
  → UPDATE status=1, published_at=now
KafkaOrderConsumerWorker:
  → INSERT IGNORE idempotency_key (same tx as business logic)
  → Decrease DB stock
  → Insert order row
  → Update order_queue.status=1
```

## 7. Configuration

| Java (application.yml) | .NET (appsettings.json) |
|------------------------|-------------------------|
| `server.port: 1122` | `"Urls": "http://*:5080"` (Program.cs) |
| `spring.datasource.*` | `ConnectionStrings:MySql` |
| `spring.data.redis.*` | `Redis:ConnectionString` |
| `spring.kafka.*` | `Kafka:*` |
| `resilience4j.circuitbreaker.*` | Polly v8 ResiliencePipeline (TASK-009) |
| `vnPay.*` (future) | `VnPay:*` |

## 8. Observability

- **Logs**: Serilog → Console (configurable sink). CorrelationId via `LogContext` middleware (TASK-010).
- **Metrics**: prometheus-net → `/metrics`. ASP.NET Core built-in `MapMetrics`.
- **Health**: `/health` (basic) → expanded in TASK-010 (MySQL/Redis/Kafka liveness).

## 9. Current state

Phase 0 (TASK-001..010) + Phase 1 first + second + third slices (TASK-011 catalog, TASK-012 order read, TASK-013 order CAS) complete.
`/health` + `/metrics` + `/ticket/*` (10 endpoints) + 3 `/order/{userId}/*` reads + 4 order CAS routes (`POST /order/cas`, `GET /order/{ticketId}/{quantity}/order|cas|queued`) + `PUT /order/{userId}/{orderNumber}/cancel` (TASK-014) are live. The remaining tasks (TASK-015..020) bring OrderMQ producer/consumer/publisher, Payment, Employee, Booking controllers online. Stubs remain wired for tasks not yet started.

Next step: TASK-015 — order MQ producer (Lua pre-deduct + insert `order_queue` + `outbox_event` in 1 tx).

## 10. API Endpoints

This table lists every HTTP route the .NET backend exposes, mirroring the Java
controller surface. Each row is filled in by the task that lands the endpoint.
Behaviour parity is verified in TASK-021 via golden-JSON comparison.

| Method | Route | Controller | .NET task | Status | Java source |
|--------|-------|------------|-----------|--------|-------------|
| GET | `/ticket/active` | `TicketController.GetAllActiveAsync` | TASK-011 | ✅ done | `TicketController.java:38` |
| POST | `/ticket/create` | `TicketController.CreateAsync` | TASK-011 | ✅ done | `TicketController.java:74` |
| GET | `/ticket/{id}` | `TicketController.GetByIdAsync` | TASK-011 | ✅ done | `TicketController.java:108` |
| PUT | `/ticket/{id}` | `TicketController.UpdateAsync` | TASK-011 | ✅ done | `TicketController.java:130` (Java no-op; .NET persists) |
| PUT | `/ticket/{id}/active` | `TicketController.ActivateAsync` | TASK-011 | ✅ done | `TicketController.java:155` |
| PUT | `/ticket/{id}/inactive` | `TicketController.DeactivateAsync` | TASK-011 | ✅ done | `TicketController.java:176` |
| DELETE | `/ticket/{id}` | `TicketController.DeleteAsync` | TASK-011 | ✅ done | `TicketController.java:197` |
| GET | `/ticket/{ticketId}/detail/{detailId}` | `TicketDetailController.GetDetailAsync` | TASK-011 | ✅ done | `TicketDetailController.java:56` |
| GET | `/ticket/{ticketId}/detail/{detailId}/order` | `TicketDetailController.OrderByUserAsync` | TASK-011 | ✅ done (raw `true`) | `TicketDetailController.java:71` (raw bool, quirk preserved) |
| GET | `/ticket/ping/java` | `TicketDetailController.PingAsync` | TASK-011 | ✅ done (1s sleep) | `TicketDetailController.java:23` |
| GET | `/order/{ticketId}/{quantity}/order` | `OrderController.PlaceOrderCasGetAsync` | TASK-013 | ✅ done | Java parity (TBD) |
| GET | `/order/{ticketId}/{quantity}/cas` | `OrderController.DecreaseStockLevel3CasAsync` | TASK-013 | ✅ done | Java parity (TBD) |
| GET | `/order/{ticketId}/{quantity}/{userId}/queued` | `OrderController.DecreaseStockQueueAsync` | TASK-013 | ✅ done (501, TASK-015 stub) | Java parity (TBD) |
| GET | `/order/{userId}/list` | `OrderController.ListByUserAsync` | TASK-012 | ✅ done | Java parity (TBD) |
| GET | `/order/{userId}/list/page` | `OrderController.ListPageByUserAsync` | TASK-012 | ✅ done | Java parity (TBD) |
| GET | `/order/{userId}/{orderNumber}` | `OrderController.GetByOrderNumberAsync` | TASK-012 | ✅ done | Java parity (TBD) |
| POST | `/order/cas` | `OrderController.PlaceOrderCasAsync` | TASK-013 | ✅ done | Java parity (TBD) |
| PUT | `/order/{userId}/{orderNumber}/cancel` | `OrderController.CancelOrderAsync` | TASK-014 | ✅ done | Java `TicketOrderController.cancelOrder` — distributed lock `LOCK:CANCEL_ORDER:{orderNumber}` (wait 1 s, expiry 5 s) + idempotent on already-CANCELLED + DB stock restore + best-effort Redis stock restore. Always HTTP 200 with `success:true|false` (matches `KNOWN_DIFFERENCES.md` §4). |
| POST | `/api/bookings` | `BookingController` | TASK-020 | pending | Java TBD |
| GET | `/hello/hi`, `/hello/hi/v1`, `/hello/circuit/breaker` | `HiController` | TASK-020 | pending | Java TBD |
| POST | `/api/v1/secure/data`, `GET /api/v1/secure/info` | `SecureApiController` | TASK-020 | pending | Java TBD |
| POST | `/employee/sign-in`, `GET /employee/month/{yyyyMM}` | `EmployeeController` | TASK-019 | pending | Java TBD |
| POST | `/payment/create`, `GET /payment/callback` | `PaymentController` | TASK-018 | pending | Java TBD |

Health & infra:

| Method | Route | Handler | Source |
|--------|-------|---------|--------|
| GET | `/health` | minimal API | `Program.cs` (TASK-009; expanded in TASK-010) |
| GET | `/metrics` | prometheus-net `MapMetrics` | `Program.cs` |
| GET | `/swagger`, `/swagger/index.html` | Swashbuckle (dev only) | `Program.cs` |