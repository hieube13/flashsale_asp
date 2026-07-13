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
| catalog (read) | `TicketOrderAppServiceImpl.findAll/findPage/findByOrderNumber` | `FlashSale.Application.Services.TicketOrderAppService` | 🟡 TASK-012 |
| order | `TicketOrderAppServiceImpl.placeOrderCAS` | same | 🟡 TASK-013 |
| order | `cancelOrder` | same | 🟡 TASK-014 |
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
| `ticket_order_{yyyyMM}` | Monthly shard for orders | Dapper dynamic table |
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

This is the **Phase 0 scaffold**. All concrete service implementations throw `NotImplementedException` until their respective TASK-011..020 ships. The solution compiles, the API boots, `/health` and `/metrics` respond.

Next step: TASK-011 — implement `ITicketAppService` to port `TicketAppServiceImpl` from Java.