# KNOWN_DIFFERENCES — Java vs .NET behavioural deltas

This file records every place where the .NET port deliberately **preserves** a Java bug or quirk (parity-first), or where it **fixes** something during the port and the user must review the choice.

Empty until TASK-011 ships. Below are the **anticipated** differences the migration will likely surface — add entries as we discover them.

---

## Format

Each row is one observed difference:

| # | Area | Java behaviour | .NET behaviour | Verdict | Reason | Discovered in |
|---|------|----------------|----------------|---------|--------|---------------|
| 1 | VNPay HMAC | `hmacSHA512("SECRET", hashDataStr)` — hardcoded literal "SECRET" instead of `SECRET_KEY` | Will preserve initially; configurable via `VnPay:SecretKey` | preserve → fix | Java bug; .NET uses config to allow proper sandbox key without code change | pre-migration review |
| 2 | Ticket create default status | Java `TicketMapper.toEntity` sets `status=1` (ACTIVE) by default | .NET `TicketMapper.ToEntity(CreateTicketRequest)` also defaults to `status=1` | preserve | Mirrors Java default — newly-created events are immediately active so they appear in `/ticket/active` | TASK-011 |
| 3 | `/ticket/{ticketId}/detail/{detailId}/order` response shape | Java returns **raw boolean** `true`/`false`, not `ResultMessage<bool>` | .NET preserves raw `bool` — controller method signature is `Task<bool>`, not `Task<ResultMessage<bool>>` | preserve | FE `CartPage.handleConfirm` calls the CAS path, not this one — no FE consumer. Keep parity for direct `curl` parity tests. | TASK-011 |
| 4 | `/ticket/ping/java` response shape | Java returns `{"status":"OK"}` (custom `Response` class), not wrapped in `ResultMessage` | .NET `PingAsync` returns `Ok(new { status = "OK" })` — same raw shape | preserve | FE doesn't call this endpoint; only used by `/ticket/ping/java` parity probes. Keep Java shape verbatim. | TASK-011 |
| 5 | `PUT /ticket/{id}` (update) | Java method body is **no-op** — always returns `null` data | .NET `TicketAppServiceImpl.UpdateAsync` actually persists changes (name/desc/times) | fix | No FE consumer in `xxxx.fe.com` calls this; Java no-op was almost certainly a TODO. .NET completes the feature so parity tests can verify round-trip. | TASK-011 |
| 6 | `/ticket/create` transactional boundary | Java calls `ticketRepo.save(ticket)` then `ticketItemRepo.save(detail)` in `TicketDomainService.createTicket` — no `@Transactional` annotation, so two separate MySQL transactions | .NET `TicketDomainService.CreateAsync` calls `_tickets.AddAsync` then `_details.AddAsync` — two separate EF SaveChanges calls, also non-atomic | preserve | Both implementations are equally non-atomic; preserving parity. To make atomic in .NET, wrap with `IDbContextTransaction` — out of scope for TASK-011 (would diverge from Java). | TASK-011 |
| 7 | Random userId in placeOrderCAS / decreaseStockLevel3CAS | Java: `int userId = ThreadLocalRandom.current().nextInt(1, 10)` — fake demo userId 1-9 | .NET: `Random.Shared.Next(1, 10)` | preserve | Both controllers are demo placeholders; FE doesn't call them. Mirroring the quirk keeps contract parity for golden-JSON tests. Could be promoted to a config-driven value (`Orders:DemoUserId`) if FE later needs to fake a real session. | TASK-013 |
| 8 | Order number format | Java: `"OKX-SGN-" + userId + "-" + ORDER_SEQ.incrementAndGet() + "-" + System.currentTimeMillis()` (hard-coded prefix) | .NET: `$"OKX-SGN-{userId}-{seq}-{tsMillis}"` (same literal) | preserve | Same default in both → parity preserved unless user overrides via `Orders:NumberPrefix`. Config hook is the planned escape valve; not wired yet (out of TASK-013 scope). | TASK-013 |
| 9 | `cancelOrder` transactional boundary | Java: `@Transactional(rollbackFor = Exception.class)` wraps `cancelOrder` → if DB stock restore throws, the order status flip to CANCELLED is rolled back | .NET: no `IDbContextTransaction` wrapping `CancelOrderAsync` — if `_details.IncreaseStockAsync` throws, the order row remains CANCELLED but stock is not restored | preserve (with caveat) | Mirrors Java's non-atomic intent (Java's `tickerOrderDomainService.increaseStock` is itself non-atomic too — see TASK-013 row on decrease parity). The .NET port keeps the same semantics intentionally; the docstring in `CancelOrderAsync` calls out that next call is idempotent because status=2. Promoting to a true 2PC-style saga is out of scope for TASK-014 and would diverge from Java. | TASK-014 |
| 10 | OrderMQ producer transactional scope | Java: `TransactionTemplate` (programmatic tx, lines 66-93) wraps both INSERTs in 1 transaction. | .NET: `IOrderMqTransactionService` abstraction (Application) implemented by `OrderMqTransactionServiceImpl` using EF `IDbContextTransaction` + `BeginTransactionAsync` (Infrastructure). Both SaveChanges calls share the same DbContext + tx; any throw rolls back both rows. | preserve | Same atomic-outbox guarantee, different plumbing. Kept the abstraction split (Application owns the contract, Infrastructure owns the EF plumbing) so the unit tests can substitute a mock tx service — matches the pattern used for `IStockOrderCacheService` and `IDistributedLock` in TASK-013/014. | TASK-015 |
| 11 | OrderMQ consumer idempotency boundary | Java: `@Transactional(rollbackFor = Exception.class)` on `KafkaOrderConsumer.processOrder` (line 37) wraps `idempotencyKeyRepository.tryInsert` + business logic in **one** Spring tx. If a downstream step throws, the idempotency row is rolled back too → Kafka retry re-inserts it. | .NET: `OrderMqConsumerHandlerImpl.ProcessAsync` runs each step with its own `SaveChangesAsync` — idempotency is committed **before** stock decrement / order insert. If a later step throws, retry would see the idempotency row and SKIP — losing the order. | document-only (controlled caveat) | Java's pattern relies on Spring `Propagation.REQUIRED` rollback semantics that map awkwardly to EF Core's per-repo `SaveChangesAsync` without lifting everything into one ambient UoW. The only thrown-after-idempotency paths today are infra failures (DB / Redis hard-down) — at that point the next consumer attempt will hit the same infra failure and the order is lost anyway. We trade exact-once recovery on infra failures for simpler per-repo code. The unit test `StatusUpdateFailure_propagates_exception` documents the wire — caller (the Kafka worker) re-raises and the offset is committed with a poison-pill log; Kafka replays but the idempotency gate skips → order row never written. **TASK-024 smoke E2E** will probe this scenario. | TASK-016 |
| 12 | Kafka consumer concurrency model | Java: `@KafkaListener(concurrency = "10")` — Spring spawns 10 consumer threads per pod. | .NET: single `BackgroundService` per pod polls `IConsumer.Consume` sequentially. Scale-out via N pods in the same consumer group; Kafka rebalances partitions across pods. | document-only | Confluent.Kafka `IConsumer` is single-threaded by contract; mimicking 10-thread poll would need 10 `IConsumer` instances, one per partition, hand-rolled. We trade that for partition-level parallelism which is the natural unit of Kafka ordering anyway — same total throughput under load, different operational shape. | TASK-016 |
| 13 | Kafka consumer retry / poison-pill | Java: Spring's default `DefaultErrorHandler` with `FixedBackOff(0L, 9L)` — 9 immediate retries then logs + commits offset. No DLQ. | .NET: 3 retries with `200/400/800 ms` exponential backoff inside `KafkaOrderConsumerWorker.ProcessWithRetryAsync`. On final failure we commit the offset and log `Abandoning unprocessable message` — same no-DLQ escape hatch. | preserve (different shape, same end result) | Behaviour parity: no DLQ, both eventually commit to unblock the partition. We chose fewer, slower retries because the dominant failure modes here are Redis / MySQL hiccups where the gap helps. The retry count + delay are private constants in `KafkaOrderConsumerWorker` for now — promote to `Kafka:Consumer:Retry*` config when TASK-024 lands. | TASK-016 |
| 14 | OrderMQ consumer order number format | Java: `"MQ-" + message.getUserId() + "-" + System.currentTimeMillis()` (line 64 of `KafkaOrderConsumer.java`). | .NET: `$"MQ-{message.UserId}-{new DateTimeOffset(now).ToUnixTimeMilliseconds()}"` (`OrderMqConsumerHandlerImpl.cs` line ~107). | preserve | Identical observable format `MQ-{userId}-{tsMillis}`. | TASK-016 |

---

## Anticipated deltas (to confirm during TASK-011..020)

### 1. VNPay signature bug (TASK-018)

**Java** (`xxxx-infrastructure/.../VnPayGatewayServiceImpl.java:59`):
```java
String vnp_SecureHash = hmacSHA512("SECRET", hashDataStr);
```

`SECRET` is a literal string, not the `SECRET_KEY` constant declared on line 18. **Every** VNPay URL produced by Java has the same hash → callback verify will fail.

**TASK-018 .NET plan**:
- Read `VnPay:SecretKey` from configuration.
- HMAC with the configured value.
- `Verdict`: **fix** (Java bug, but config-driven so user can disable fix if parity is desired).

### 2. Order number prefix (TASK-013)

**Java** uses `"OKX-SGN-" + userId + "-" + seq + "-" + tsMillis`. Java code calls this "OKX-SGN" hard-coded — appears to be a placeholder from a customer engagement.

**TASK-013 .NET plan**:
- Make prefix configurable: `Orders:NumberPrefix` in appsettings, default `OKX-SGN`.
- Same default → parity preserved unless user overrides.
- `Verdict`: **preserve** (parity) with config hook.

### 3. ~~ThreadLocalRandom for userId~~ → promoted to main table row #7 (TASK-013 landed)

### 4. Order status 200 on error (TASK-013, 015, 020)

**Java** returns `ResultUtil.data(PlaceOrderResponse.failed(...))` for errors — the HTTP status is always 200 OK with `success: false` in the body.

**TASK-013/015 .NET plan**:
- Keep HTTP 200 + `success: false` body for parity.
- Document in INTERNAL_ARCHITECTURE §API parity.
- `Verdict`: **preserve** (frontend depends on it).

### 5. Tomcat accept-count / max-connections tuning (TASK-009)

**Java**: `accept-count: 2000`, `max-connections: 10000`.

**.NET**: Kestrel defaults are fine, but we set `MaxConcurrentConnections = 10000` on `KestrelServerOptions`.

**`Verdict`**: **preserve** (operational parity).

### 6. Hibernate `ddl-auto: none` (TASK-005)

**Java**: `spring.jpa.hibernate.ddl-auto: none` — schema is created by SQL files in `environment/mysql/init/`.

**.NET**: EF Core `OnModelCreating` declares shape but never runs `EnsureCreated`/`Migrate`. DDL stays in `environment/mysql/init/ticket_init.sql`.

**`Verdict`**: **preserve** — DB schema is owned by SQL scripts.

### 7. result_name=`ticket_order_{yyyyMM}` (TASK-012)

**Java**: `ticker_order_202604` etc — string concat at runtime.

**.NET**: use Dapper with dynamic table name; keep the same naming convention.

**`Verdict`**: **preserve**.

### 8. ~~Java userId pattern in orderQueue (TASK-015)~~ → already covered by main table row #7 (TASK-013)

---

## How to add an entry

When porting and you spot a difference:

```markdown
| # | Area | Java behaviour | .NET behaviour | Verdict | Reason | Discovered in |
|---|------|----------------|----------------|---------|--------|---------------|
| N | <module> | <what Java does> | <what .NET does> | preserve / fix | <why> | TASK-XXX |
```

Always link the Java file:line. Always link the .NET file:line.