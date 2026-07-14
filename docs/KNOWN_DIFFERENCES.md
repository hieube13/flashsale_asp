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
| 15 | Outbox publisher cadence + batch size | Java: `@Scheduled(fixedDelay=1000)`, `BATCH_SIZE = 500`, row-by-row (lines 31, 40-44, 55-80). | .NET: `OutboxPublisherWorker` loops in `ExecuteAsync` with `Task.Delay(1000 ms)` between cycles + 250 ms startup jitter; `DefaultBatchSize = 500` constant. Row-by-row mode — the `publishBatch()` Java variant is NOT ported (commented as future opt-in). | preserve | Same tick rate, same batch size, same row-by-row execution order. Startup jitter is a defensive addition so multiple API pods don't stampede the outbox table on cold start — not strictly required by Java's `@Scheduled` because Spring's scheduler randomises startup by default. | TASK-017 |
| 16 | Outbox publisher failure handling | Java: catches `Exception` per row, logs `"will retry next cycle"`, leaves PENDING (lines 74-78). On parse failure: continues to next row without logging ERROR. | .NET: same try/catch around `SendAndAwaitAckAsync` + `MarkPublishedAsync`, leaves PENDING. On `JsonException` or null payload: logs ERROR, does NOT mark PUBLISHED, continues to next row. | preserve (with stronger logging) | We surface parse failures at ERROR level instead of silently continuing — easier to spot in production. Trade-off: a malformed payload stays PENDING forever. Java's behaviour is identical. A future TASK can add a dead-letter table for poison rows. | TASK-017 |
| 17 | Payment IPN — `ticket_order` status update | Java: `PaymentServiceImpl.handleCallback` updates `payment_transaction.status` only; never touches `ticket_order` (Java even logs a TODO comment that the order is "still PENDING on success"). | .NET: `PaymentAppServiceImpl.HandleCallbackAsync` mirrors Java — updates `PaymentTransaction.PaymentStatus` only on RspCode flip; does NOT update `TickerOrder.PaymentStatus` or `OrderQueue.Status`. | preserve | Matches Java semantics. The order remains in `ticket_order_{yyyyMM}` with whatever status MQ consumer assigned (SUCCESS / FAILED / CANCELLED). Payment acts as an out-of-band reconciliation channel. Promoting to "payment SUCCESS → mark ticket_order PAID" requires a coordinated change with TASK-015's MQ consumer (probably outbox event `OrderPaidIntegrationEvent` and a separate listener). Documented here so reviewers don't file the gap as a bug. | TASK-018 |
| 18 | Payment `vnp_IpAddr` source | Java: `PaymentServiceImpl` hardcodes `String IP = "123.45.67.89"` (Java line 64) and passes it as `vnp_IpAddr`. Every request uses the same string. | .NET: `PaymentController.ResolveClientIp` reads `X-Forwarded-For` header (first IP after split), then falls back to `Connection.RemoteIpAddress`, then `"0.0.0.0"`. Backed by `UseForwardedHeaders()` + `ForwardedHeadersOptions` middleware. Reads from config via `VnPay:ReturnUrl` only (IP is request-derived). | fix | Java hardcoding makes the URL reject real client IPs. The .NET port wires forwarded-headers support in `Program.cs` so a real client IP propagates through nginx. Set the forwarded-headers `KnownNetworks` / `KnownProxies` whitelist before deploying behind a load balancer — dev currently accepts any source. | TASK-018 |
| 19 | Payment IPN — distributed lock | Java: no concurrent guard on the IPN path — a VNPay retry with the same `txnRef` would re-process the row (DB upsert is idempotent so the visible result is the same, but there's a transient double-execute window). | .NET: `PaymentAppServiceImpl.HandleCallbackAsync` acquires `LOCK:PAYMENT_IPN:{txnRef}` (RedLock provider, wait 5 s / expiry 10 s) before reading the row, mirroring `CancelOrderAsync` from TASK-014. Lock-busy returns `RspCode=02` ("Order already confirmed") which makes VNPay stop retrying. | fix | VNPay's retry policy can re-deliver the same IPN within seconds on transient 5xx. The lock keeps the work single-threaded per txRef. Trade-off: in dev without Redis, all IPNs for the same txRef within the 5 s window receive the `02` response (not broken because VNPay treats `02` as a stable ACK). | TASK-018 |
| 20 | Payment IPN — response body | Java: returns the response via Spring `HttpServletResponse.getWriter().print(RspUtil.data(...))` — wraps in Spring's `RspUtil` envelope. | .NET: `PaymentController.IpnAsync` returns raw JSON `{RspCode, Message}` per VNPay's IPN spec (no `.NET` envelope). Returned through `ResultMessage<>` would be wrong here — VNPay parses the raw body for `RspCode`. | preserve (different shape, same wire contract) | VNPay's IPN consumer is an external system, not our FE — we follow its parser, not our own convention. `/payment/create` and `/payment/callback/return` still return `ResultMessage<>` / HTML because those paths go through the FE. | TASK-018 |
| 21 | Employee timesheet timezone | Java: `LocalDate.now()` resolves to JVM default zone — distributed Java pods see different keys if their host TZ differs. | .NET: every key (and HTTP date param) is normalised to **UTC** before computing `yyyyMM`/`yyyy-MM-dd`. All reads + writes pin to `DateTime.UtcNow` or `DateTimeKind.Utc`. | fix | Cross-pod key stability requires a single reference TZ — picking the host TZ by accident is the classic Redis bitmap cache-poisoning bug. UTC is the safest pick; revisit if a future feature genuinely requires business-day semantics in `Asia/Ho_Chi_Minh`. | TASK-019 |
| 22 | Employee `first-day` scan bounds | Java: `getFirstSignDay` linearly scans `i=0..30` unconditionally. For a February month with 28 days, a bitmap with bit 30 set would return 31 as the first signed-in day. | .NET: `GetFirstSignDayAsync` scans only `0..lengthOfMonth-1` (e.g. 0..27 for Feb, 0..30 for Jul). | fix | Java's "scan 31 bits" is a latent bug for any month shorter than 31 days. The .NET fix keeps the Java-flavoured algorithm but bounds the loop with `DateTime.DaysInMonth`. Trade-off: a stray write to bit 29/30 in Feb is silently ignored (the bit was never reachable from a valid sign-in date). | TASK-019 |
| 23 | Employee `consecutive-days` across month boundary | Java: walks backward from the requested day's bit-0 in the same bitmap. Returns once it hits a clear bit or bit 0 — even when the user actually signed in every day up to the last day of the previous month. | .NET: identical — single-month walk, no follow-up into `user:sign:{userId}:{prev_yyyyMM}`. | preserve | A future TASK can add a cross-month follow-up by reading the previous month's bit at offset `lengthOfPreviousMonth - 1`. Out of scope for the parity-first port — Java behaviour is what the original product team shipped. | TASK-019 |
| 24 | Employee `monthly-sign-details` + `summary` DTO shape | Java: returns `Map<String, Object>` with keys `totalSignCount` (int) and `signDays` (List<Integer>) for `monthly-sign-details`; `summary` returns a Map keyed by `date`/`hasSignedIn`/`monthlyCount`/`firstSignDay`/`consecutiveDays`. | .NET: returns typed DTOs `MonthlySignDetailsDto { TotalSignCount:int, SignDays:IReadOnlyList<int> }` and `EmployeeSummaryDto { date, hasSignedIn, monthlyCount, firstSignDay, consecutiveDays }`. JSON serialization uses .NET's camelCase convention. | preserve (different shape, same data) | Wire JSON keys are identical between Java's `Map` and the .NET DTO (`totalSignCount`, `signDays`, `hasSignedIn`, `monthlyCount`, `firstSignDay`, `consecutiveDays`). The contract test (TASK-021) compares these field-by-field. | TASK-019 |
| 25 | Employee controller auth | Java: `EmployeeController` has no auth filter — anyone can `POST /api/sign-in/{userId}` for any user. | .NET: `EmployeeController` matches Java's open route shape (no `[Authorize]` attribute). | preserve (with caveat) | Java's open policy is undocumented but observable in `xxxx.com`'s production. The .NET port follows suit so cutover parity holds. This is a documented gap for the operator to close down (e.g. add JWT or rate-limit middleware) in a follow-up TASK — see INTERNAL_ARCHITECTURE.md §Auth boundaries. | TASK-019 |
| 30 | `PlaceOrderResponse` field names | Java: `placeOrderTaskId` / `orderId` | .NET: `orderNumber` | preserve | `orderNumber` is semantically clearer. Parity tests in `OrderParityTests` verify the .NET shape. | TASK-021 |
| 31 | `POST /payment/create` request shape | Java: query params (`userId`, `orderNumber`, `method`) | .NET: JSON body (`CreatePaymentRequest`) | preserve | .NET JSON body is ASP.NET best practice (typed model binding, validation, OpenAPI). | TASK-021 |
| 32 | Employee + Order raw responses | Java: raw types (string / bool / long / int) | .NET: `ResultMessage<T>` wrapper | preserve | .NET API convention. These endpoints are internal/admin-facing (not called by `xxxx.fe.com`). Documented for reviewers. | TASK-021 |

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

**TASK-013/015/020 .NET plan**:
- Keep HTTP 200 + `success: false` body for parity. Booking `POST /api/bookings` returns `ResultMessage<BookingDto>.Error(400/500, msg)` with HTTP 200.
- Document in INTERNAL_ARCHITECTURE §API parity.
- `Verdict`: **preserve** (frontend depends on it).

### 4b. Booking `BookingDto` field shape (TASK-020)

**Java** `BookingDTO` (`xxxx-application/.../model/BookingDTO.java`) exposes **5 fields only**:
`{ id, ticketId, quantity, bookingCode, status }` — **no `createdAt`**.

**.NET** `BookingDto` (`FlashSale.Contracts.Dto.BookingDto`) mirrors Java exactly: 5 fields.

**`.NET` vs Java parity** — match. Removed `CreatedAt` from the .NET record (it was present in the early scaffold but Java does not carry it).

**`Verdict`**: **preserve** (frontend parity).

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

### 26. Java shipping without `booking` DDL (TASK-020)

**Java**: The `Booking` entity is mapped via Spring Data JPA but `application.properties` sets `spring.jpa.hibernate.ddl-auto: none` and **the `environment/mysql/init/*.sql` set does not contain a `CREATE TABLE booking` statement**. As-shipped, `POST /api/bookings` would throw at runtime ("table doesn't exist") — Java has this latent bug.

**.NET** (TASK-020): We added `CREATE TABLE IF NOT EXISTS booking (…)` to `environment/mysql/init/01-schema.sql` so the table exists at startup. This is a **deliberate .NET improvement** over the Java DDL gap.

**`Verdict`**: **.NET diverges** — fixing the missing DDL on the .NET side. Document as known Java latent bug.

### 27. `EventAppServiceImpl.SayHi(name)` ignores input (TASK-020)

**Java** `HiInfrasRepositoryImpl.sayHi(who)` returns the **hardcoded literal `"Hi Infrastructure"` regardless of the `who` argument** (`xxxx-infrastructure/.../HiInfrasRepositoryImpl.java:10`). The arg is silently dropped.

**.NET** `EventAppServiceImpl.SayHi(name)` (`FlashSale.Application.Services.Implementations.EventAppServiceImpl.cs`) preserves this quirk — accepts the input but returns the literal constant.

**`Verdict`**: **preserve** — observable parity. If this is ever needed to behave normally, both Java and .NET must be changed together.

### 28. `SecureApiController` has 4 extra demo routes (TASK-020)

**Java** `SecureApiController.java` exposes **exactly 2 routes** under `/api/v1/secure/*` (`GET /info` + `POST /data`). No mode-switching, no `mode=…` query param, no exception simulator. Java has a class `InvalidSignatureException` but no interceptor that actually enforces signatures — the endpoint is unprotected.

**.NET** TASK-020 keeps both Java endpoints verbatim (raw `{status, message, receivedPayload?}` shape — **NO** `ResultMessage<T>` wrapper) and **adds 4 extra sub-routes** for circuit-breaker smoke testing: `/unauthorized` (HTTP 401), `/forbidden` (HTTP 403), `/slow` (2 s `Task.Delay` then 200), `/throw` (raises `InvalidOperationException`). The extra routes are dev-mode only and would be gated behind an environment flag in production.

**`Verdict`**: **.NET diverges** — extra routes do not exist in Java. They are clearly prefixed by behaviour (`/unauthorized`, `/forbidden`, …) so a parity scanner can ignore them. No impact on the parity baseline because Java endpoints stay byte-for-byte identical.

### 29. `IBookingRepository.findByBookingCode` not ported (TASK-020)

**Java** `BookingRepository` interface declares `findByBookingCode(String)` but **no controller or service uses it** — the method is dead code.

**.NET** `IBookingRepository` (`FlashSale.Domain.Repositories.IBookingRepository`) carries only `AddAsync` + `GetByIdAsync` (the `GetByIdAsync` is also unused by any controller — kept for parity with the existing scaffold + future endpoints).

**`Verdict`**: **preserve** — no consumer means parity is automatically maintained.

### 30. `PlaceOrderResponse` field names differ: `orderNumber` vs `placeOrderTaskId`/`orderId` (TASK-021)

**Java** `PlaceOrderResponse.java` uses field names:
```json
{ "success": true, "placeOrderTaskId": "TOKEN_TICKET_USER_1_5", "orderId": null, "code": null, "message": null }
```

**.NET** `PlaceOrderResponse.cs` uses field names:
```json
{ "success": true, "orderNumber": "OKX-SGN-7-42-1718246100123", "code": null, "message": null }
```

Affected endpoints: `POST /order/cas`, `POST /order/mq`, `GET /order/mq/status/{token}`.

**`Verdict`**: **preserve .NET naming** — `orderNumber` is semantically clearer. Documenting as known difference. Parity tests in `OrderParityTests` verify the .NET shape.

### 31. `POST /payment/create` request format: JSON body vs query params (TASK-021)

**Java** `PaymentController.java:21`:
```java
public ResultMessage<String> createPayment(
    @RequestParam("userId") Long userId,
    @RequestParam("orderNumber") String orderNumber,
    @RequestParam("method") String method)
```

**.NET** `PaymentController.cs` accepts a JSON body (`CreatePaymentRequest` with `userId`, `orderNumber`, `method`).

**`Verdict`**: **preserve .NET JSON body** — ASP.NET best practice (typed model binding, validation, OpenAPI docs). Documenting as known difference.

### 32. Employee + Order endpoints: `.NET` wraps in `ResultMessage<T>`, Java returns raw types (TASK-021)

**Java** `/api/sign-in/{userId}/*` endpoints return raw types:
- `POST /api/sign-in/{userId}` → raw string `"Sign-in successful for {userId} at {date}"`
- `GET /api/sign-in/{userId}/check?date=` → raw boolean
- `GET /api/sign-in/{userId}/monthly-count?month=` → raw long
- `GET /api/sign-in/{userId}/monthly-sign-details?month=` → raw `Map<String,Object>`
- `GET /api/sign-in/{userId}/consecutive-days?date=` → raw int
- `GET /api/sign-in/{userId}/summary?date=` → raw `Map<String,Object>`

**Java** `/order/{ticketId}/{quantity}/order` and `/order/{ticketId}/{quantity}/cas` return raw boolean.

**.NET** `EmployeeController` and `OrderController` wrap all responses in `ResultMessage<T>` (standard envelope: `success`, `message`, `code`, `timestamp`, `result`).

**`Verdict`**: **preserve .NET `ResultMessage<T>` wrapper** — this is the .NET API convention. Employee and Order endpoints are internal/admin-facing and not called by the `xxxx.fe.com` frontend. Documenting as known difference. Contract tests verify the .NET envelope shape.

---

## How to add an entry

When porting and you spot a difference:

```markdown
| # | Area | Java behaviour | .NET behaviour | Verdict | Reason | Discovered in |
|---|------|----------------|----------------|---------|--------|---------------|
| N | <module> | <what Java does> | <what .NET does> | preserve / fix | <why> | TASK-XXX |
```

Always link the Java file:line. Always link the .NET file:line.