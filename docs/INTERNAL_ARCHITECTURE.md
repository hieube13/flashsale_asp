# INTERNAL_ARCHITECTURE — Internal code structure

This is the `.NET`-side counterpart of [FLASH_SALE_ARCHITECTURE.md](FLASH_SALE_ARCHITECTURE.md).
It documents how the project is laid out, the dependency direction, naming, and the
"why" behind each design choice — so any task contributor can drop in without a tour.

## 1. Project dependency graph

```
                       ┌──────────────────────────┐
                       │       FlashSale.Api      │
                       │  (controllers, workers)  │
                       └─────────────┬────────────┘
                                     │ refs
                ┌────────────────────┼─────────────────────┐
                ▼                    ▼                     ▼
     ┌─────────────────────┐ ┌──────────────────┐ ┌──────────────────┐
     │   Application       │ │  Infrastructure  │ │   Contracts      │
     │ (service interfaces)│ │ (EF, Redis, Kafka│ │ (DTOs, messages) │
     │                     │ │  distributed lock│ │                  │
     └──────────┬──────────┘ └────────┬─────────┘ └──────────────────┘
                │ refs                │ refs
                └────────┬───────────┘
                         ▼
                  ┌────────────────────┐
                  │      Domain        │
                  │ (entities, enums,  │
                  │  repository ifaces)│
                  │ (no dependencies!) │
                  └────────────────────┘
```

Rules enforced by `tests/FlashSale.ArchitectureTests`:

| From | Allowed to reference | Forbidden |
|------|----------------------|-----------|
| Domain | (nothing) | Application, Infrastructure, Api, Contracts |
| Contracts | (nothing) | Domain, Application, Infrastructure, Api |
| Application | Domain, Contracts | Infrastructure, Api |
| Infrastructure | Application, Domain, Contracts | Api |
| Api | Application, Infrastructure, Domain, Contracts | — |

## 2. Project structure

```
src/
├── FlashSale.Domain/
│   ├── Entities/
│   │   ├── Ticket.cs
│   │   ├── TicketDetail.cs
│   │   ├── TickerOrder.cs
│   │   ├── OrderQueue.cs
│   │   ├── OutboxEvent.cs
│   │   ├── IdempotencyKey.cs
│   │   ├── PaymentTransaction.cs
│   │   └── Booking.cs
│   ├── Enums/
│   │   ├── OrderStatus.cs
│   │   ├── OrderQueueStatus.cs
│   │   └── OutboxStatus.cs
│   ├── Repositories/            # interface only — impl in Infrastructure
│   │   └── IRepositories.cs     # 8 interfaces bundled to keep small
│   └── Services/                # domain service interfaces
│       └── IOrderDeductionDomainService.cs
│
├── FlashSale.Contracts/
│   ├── Dto/
│   │   ├── ResultMessage.cs         # ResultMessage<T> + ResultMessage
│   │   ├── ResultCode.cs            # HTTP + domain error codes (shared)
│   │   ├── PlaceOrderResponse.cs    # success/Code/Message envelope
│   │   ├── CreateBookingRequest.cs
│   │   ├── PlaceOrderMqRequest.cs
│   │   ├── TicketRequests.cs        # CreateTicketFullRequest, etc.
│   │   └── TicketDtos.cs            # TicketDto, TicketDetailDto, TicketOrderDto, PagedOrdersDto, BookingDto
│   └── Messages/
│       └── PlaceOrderMqMessage.cs   # Kafka message payload
│
├── FlashSale.Application/
│   └── Services/                    # interfaces only — impls added per task
│       ├── ITicketAppService.cs
│       ├── ITicketOrderAppService.cs
│       ├── IOrderMqAppService.cs    # + IOrderMqConsumerHandler
│       ├── IPaymentAppService.cs
│       ├── IBookingAppService.cs       # + IEmployeeCacheService, IEventAppService
│       ├── IStockOrderCacheService.cs  # Redis Lua stock cache abstraction (TASK-013)
│       ├── IDistributedLock.cs         # Distributed lock abstraction (TASK-014) — impl in Infrastructure
│       ├── IOrderMqAppService.cs       # Async place-order service contract (TASK-015)
│       ├── IOrderMqTransactionService.cs  # Atomic outbox + order_queue tx wrapper (TASK-015) — impl in Infrastructure
│       └── IOrderMqConsumerHandler.cs     # Consumer pipeline orchestrator (TASK-016) — impl in Application/Services/Implementations/
│
├── FlashSale.Infrastructure/
│   ├── Data/
│   │   ├── FlashSaleDbContext.cs         # EF Core — 8 entity sets
│   │   ├── IDbConnectionFactory.cs
│   │   └── MySqlConnectionFactory.cs
│   ├── Data/Dynamic/                      # Dapper-based dynamic tables (TASK-012)
│   │   └── TickerOrderRepositoryImpl.cs   # ticket_order_{yyyyMM} reads + Dapper inserts
│   ├── Cache/
│   │   ├── IRedisInfrasService.cs        # StackExchange.Redis abstraction
│   │   ├── RedisInfrasService.cs
│   │   ├── IStockOrderCacheService.cs    # [moved to Application/Services in TASK-013]
│   │   ├── StockOrderCacheService.cs     # Lua atomic decrement (TASK-013)
│   │   ├── ITicketCacheService.cs        # PRO_TICKET:{id} Redis cache (TASK-011)
│   │   ├── TicketCacheService.cs         # concrete (TASK-011)
│   │   └── TicketDetailCacheService.cs   # 2-tier L1 Memory + L2 Redis (TASK-011)
│   ├── Persistence/
│   │   ├── OrderQueueRepositoryImpl.cs        # EF Core order_queue CRUD (TASK-015)
│   │   ├── OutboxEventRepositoryImpl.cs      # EF Core outbox_event CRUD (TASK-015)
│   │   ├── OrderMqTransactionServiceImpl.cs  # EF IDbContextTransaction wrapper for atomic outbox write (TASK-015)
│   │   ├── IdempotencyKeyRepositoryImpl.cs   # Dapper INSERT IGNORE idempotency gate (TASK-016)
│   │   ├── Repositories/TicketRepositoryImpl.cs        # EF Core Ticket (TASK-011)
│   │   └── Repositories/TicketDetailRepositoryImpl.cs  # EF Core TicketDetail + FOR UPDATE CAS (TASK-011)
│   ├── DistributedLock/
│   │   ├── IDistributedLock.cs                # [moved to Application/Services in TASK-014]
│   │   ├── RedLockDistributedLockProvider.cs  # Real RedLock.net impl (TASK-014)
│   │   └── RedLockFactoryBuilder.cs           # Wraps RedLockNet.SERedis.RedLockFactory.Create (TASK-014)
│   ├── Messaging/
│   │   ├── IKafkaOrderProducer.cs
│   │   ├── KafkaOrderProducer.cs         # Confluent.Kafka producer (TASK-007)
│   │   └── OutboxPublisherWorker.cs       # BackgroundService — drains outbox_event to Kafka (TASK-017, moved from Api/Workers)
│   └── External/
│       ├── IVnPayGatewayService.cs
│       └── VnPayGatewayService.cs        # stub, full HMAC in TASK-018
│
└── FlashSale.Api/
    ├── Program.cs                          # DI wiring, Kestrel :5080, Serilog, /metrics
    ├── Stubs.cs                            # NotImplementedException stubs for not-yet-ported slices
    ├── Workers/
    │   │   ├── KafkaOrderConsumerWorker.cs     # BackgroundService — real Confluent.Kafka IConsumer loop (TASK-016)
│   │   └── WarmupDataWorker.cs             # BackgroundService — Redis cache warmup (TASK-011)
    ├── Controllers/                        # added per TASK-011..020
    │   ├── TicketController.cs             # 7 endpoints (TASK-011)
    │   └── TicketDetailController.cs       # 3 endpoints incl. /ticket/ping/java (TASK-011)
    │   ├── OrderController.cs              # 7 read + CAS + cancel endpoints (TASK-012/013/014)
    │   ├── OrderMQController.cs            # POST /order/mq + GET /order/mq/status/{token} (TASK-015)
    │   ├── PaymentController.cs            # TASK-018
    │   ├── EmployeeController.cs           # TASK-019
    │   ├── BookingController.cs            # TASK-020
    │   ├── HiController.cs                 # TASK-020
    │   └── SecureApiController.cs          # TASK-020
    ├── Middleware/                         # CorrelationId (TASK-010)
    ├── appsettings.json
    └── appsettings.Development.json

tests/
├── FlashSale.UnitTests/                    # xUnit
├── FlashSale.IntegrationTests/             # Testcontainers MySQL/Redis/Kafka
├── FlashSale.ContractTests/                # Golden-JSON vs Java baseline
├── FlashSale.ArchitectureTests/            # NetArchTest
└── FlashSale.LoadTests/                    # k6 scripts (no .NET project — folder only)
```

## 3. Naming conventions

| Element | Convention | Example |
|---------|------------|---------|
| Entity class | singular PascalCase matching table name | `Ticket`, `TickerOrder` |
| Repository interface | `I{Entity}Repository` | `ITicketRepository` |
| Repository impl | `{Entity}RepositoryImpl` (in Infrastructure) | `TicketRepositoryImpl` (TASK-005) |
| Service interface (Application) | `I{Module}AppService` | `ITicketAppService` |
| Service impl | `{Module}AppServiceImpl` | `TicketAppServiceImpl` (TASK-011) |
| Domain service interface | `I{Domain}{Thing}DomainService` | `IOrderDeductionDomainService` |
| Domain service impl | `{Domain}{Thing}DomainServiceImpl` | `OrderDeductionDomainService` (TASK-012 — ExtractYearMonth only) |
| Controller | `{Entity}Controller` (no `I` prefix) | `TicketController` |
| Worker (BackgroundService) | `{Purpose}Worker` | `OutboxPublisherWorker` |
| DTO (request) | `{Verb}{Entity}Request` | `CreateBookingRequest` |
| DTO (response) | `{Entity}Dto` or `{Verb}{Entity}Response` | `TicketDto`, `PlaceOrderResponse` |
| Result envelope | `ResultMessage<T>` | `ResultMessage<TicketDto>` |
| Worker folder | `FlashSale.Api/Workers/` | — |
| Controllers folder | `FlashSale.Api/Controllers/` | — |
| Cache folder | `FlashSale.Infrastructure/Cache/` | — |

## 4. JSON property naming

`Program.cs` configures `JsonNamingPolicy.CamelCase` globally. This means:

- C# `OrderNumber` → JSON `orderNumber` (matches Java)
- C# `TotalAmount` → JSON `totalAmount` (matches Java)
- C# `TicketId` → JSON `ticketId` (matches Java)

DO NOT add `[JsonPropertyName]` unless a Java field uses snake_case (none currently).

## 5. HTTP route parity

See `docs/EXECUTE.md` §Mapping reference → HTTP routes that must stay identical.

## 6. Redis key conventions

| Prefix | Purpose | Set by | Read by |
|--------|---------|--------|---------|
| `PRO_TICKET:{id}:*` | Active ticket stock + price (L1) | `WarmupDataBeforeEvent` | `placeOrderCAS`, `decreaseStockLevel3CAS` |
| `TICKET:{id}:*` | Legacy cache key (still used by `TicketDetailCacheServiceRefactor`) | Same | Same |
| `LOCK:CANCEL_ORDER:{orderNumber}` | Distributed lock | `cancelOrder` | `cancelOrder` |
| `TOKEN_LOCK_KEY{ticketId}` | MQ producer mutex | `decreaseStockQueue` | `decreaseStockQueue` |
| `user:sign:{userId}:{yyyyMM}` | BitSet monthly attendance | `signIn` | `hasSignedIn`, etc. |

DO NOT change these prefixes — both Java and .NET must read each other's data during
the cutover phase.

## 7. Kafka message schema

Topic: `order-place-topic` — payload = `PlaceOrderMqMessage` JSON:

```json
{
  "token": "MQ-xxxxxxxxxxxxxxxx",   // 16-char uuid prefix
  "ticketId": 1,
  "quantity": 2,
  "userId": 5,
  "unitPrice": 10000,
  "createdAt": 1718246100123
}
```

Java serialises via Jackson default. .NET serialises via System.Text.Json default.
Both produce equivalent JSON for this record-shape payload. Verify in TASK-016 with a contract test.

## 8. Transactional boundaries

| Layer | Boundary | Note |
|-------|----------|------|
| EF Core | `SaveChangesAsync` auto-wraps a single command set in a tx | Standard EF behaviour |
| Multi-write | Use `IDbContextTransaction` (EF) or `TransactionScope` | E.g. `placeOrderMQ` writes `order_queue` + `outbox_event` in 1 tx |
| Dapper + EF | Open the EF transaction, hand the `DbConnection` to Dapper, enlist in same tx | Used by `decreaseStockLevel1` + `increaseStock` |
| Lua script | Single Redis round-trip is inherently atomic | Used by `decreaseStockCacheByLUA` |

## 9. Error envelope

All controllers wrap responses in `ResultMessage<T>`:

```json
{
  "success": true,
  "message": "OK",
  "code": 200,
  "timestamp": 1718246100123,
  "result": { ... }
}
```

This mirrors Java's `ResultUtil.data(...)` / `ResultUtil.error(...)`. Error path returns
HTTP 200 with `success: false` (matches Java behaviour — see KNOWN_DIFFERENCES §4).

## 10. Logging

Serilog is configured in `Program.cs`:

```csharp
builder.Host.UseSerilog((ctx, lc) =>
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext()
      .WriteTo.Console());
```

Each log line includes `RequestId` (auto via `UseSerilogRequestLogging`) and
`CorrelationId` (set by middleware in TASK-010).

## 11. Status

| Slice | Task | Status |
|-------|------|--------|
| Catalog (Ticket CRUD + L1/L2 cache) | TASK-011 | ✅ done (2026-07-13) |
| Order read (Dapper dynamic table) | TASK-012 | ✅ done (2026-07-14) |
| Order CAS (Redis Lua + DB safety net) | TASK-013 | ✅ done (2026-07-14) |
| Order cancel (distributed lock) | TASK-014 | ✅ done (2026-07-14) |
| OrderMQ producer | TASK-015 | ✅ done (2026-07-14) |
| OrderMQ consumer | TASK-016 | ✅ done (2026-07-14) |
| OrderMQ publisher (outbox drain) | TASK-017 | ✅ done (2026-07-14) |
| Payment VNPay | TASK-018 | done |
| Employee timesheet | TASK-019 | done |
| Booking demo + hi + secure | TASK-020 | done |

Stubs remain in `Stubs.cs` for the slices not yet ported. Each TASK-XXX lands by
swapping one stub for its concrete impl + adding the matching controller +
worker + tests.