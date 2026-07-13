# KNOWN_DIFFERENCES — Java vs .NET behavioural deltas

This file records every place where the .NET port deliberately **preserves** a Java bug or quirk (parity-first), or where it **fixes** something during the port and the user must review the choice.

Empty until TASK-011 ships. Below are the **anticipated** differences the migration will likely surface — add entries as we discover them.

---

## Format

Each row is one observed difference:

| # | Area | Java behaviour | .NET behaviour | Verdict | Reason | Discovered in |
|---|------|----------------|----------------|---------|--------|---------------|
| 1 | VNPay HMAC | `hmacSHA512("SECRET", hashDataStr)` — hardcoded literal "SECRET" instead of `SECRET_KEY` | Will preserve initially; configurable via `VnPay:SecretKey` | preserve → fix | Java bug; .NET uses config to allow proper sandbox key without code change | pre-migration review |

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

### 3. ThreadLocalRandom for userId (TASK-013)

**Java** uses `ThreadLocalRandom.current().nextInt(1, 10)` to fake userId in `placeOrderCAS` / `decreaseStockLevel3CAS`.

**TASK-013 .NET plan**:
- Preserve in .NET using `Random.Shared.Next(1, 10)`.
- `Verdict`: **preserve** (parity — these are demo controllers).

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

### 8. Java userId pattern in orderQueue (TASK-015)

**Java** uses `ThreadLocalRandom.current().nextInt(1, 10)` for `userId` in MQ flow.

**.NET** mirrors same via `Random.Shared.Next(1, 10)`.

**`Verdict`**: **preserve**.

---

## How to add an entry

When porting and you spot a difference:

```markdown
| # | Area | Java behaviour | .NET behaviour | Verdict | Reason | Discovered in |
|---|------|----------------|----------------|---------|--------|---------------|
| N | <module> | <what Java does> | <what .NET does> | preserve / fix | <why> | TASK-XXX |
```

Always link the Java file:line. Always link the .NET file:line.