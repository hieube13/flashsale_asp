# TIMELINE — FlashSale migration roadmap

**Source**: `F:\TipJavascript\Microservice\xxxx.com-18-06-26` (Spring Boot 3.3.5 / Java 21 / DDD modular monolith / port 1122)
**Target**: `F:\TipJavascript\Microservice\flashsale` (ASP.NET Core 8 LTS / port 5080)
**Style reference**: `E:\KteamTest\Campaign_Tool\docs\*` (docs structure & granularity)

## Constraints (must hold throughout)

- C1 — Read-only on `xxxx.com-18-06-26` (Java). Never write into it from migration work.
- C2 — Every HTTP route, method, JSON property, Redis key prefix, Kafka topic, DB schema stays byte-for-byte identical.
- C3 — Java continues running on 1122. .NET runs on 5080. Cutover via nginx routing only.
- C4 — Every task produces a working `dotnet build FlashSale.slnx` + green unit/architecture tests.
- C5 — When porting, if Java contains a bug, record it in [KNOWN_DIFFERENCES.md](KNOWN_DIFFERENCES.md) instead of silently fixing.
- C6 — User reviews each task commit. AI Agent never commits on its own.

## Phases

### Phase 0 — Scaffold (TASK-001..010)

Lay down solution skeleton, contracts, infra abstractions, observability, and infrastructure-as-code so subsequent phases can drop straight into concrete code.

| Task | Title | Status |
|------|-------|--------|
| TASK-001 | solution_scaffold | ✅ done (this PR) |
| TASK-002 | shared_contracts | ✅ done |
| TASK-003 | docker_compose | ✅ done |
| TASK-004 | domain_entities | ✅ done |
| TASK-005 | infrastructure_data | ✅ done |
| TASK-006 | infrastructure_redis | ✅ done |
| TASK-007 | infrastructure_kafka | ✅ done |
| TASK-008 | application_scaffold | ✅ done |
| TASK-009 | api_foundation | ✅ done |
| TASK-010 | observability | ✅ done |

**Gate**: `dotnet build FlashSale.slnx` green, architecture tests pass, docker compose up MySQL+Redis+Kafka.

### Phase 1 — Feature slice port (TASK-011..020)

Port one Java module / use-case per task. Each task ends with green unit tests + parity check vs Java baseline.

| Task | Module | Use case |
|------|--------|----------|
| TASK-011 | catalog | Ticket CRUD + L1/L2 cache |
| TASK-012 | catalog | Order read (list / paged / by orderNumber) |
| TASK-013 | order | Order CAS slice (Redis Lua atomic + DB safety net) |
| TASK-014 | order | Order cancel (Redis lock + Redis restore) |
| TASK-015 | order-mq | Producer (Lua pre-deduct + DB order + outbox in 1 tx) |
| TASK-016 | order-mq | Consumer (idempotency gate + DB decrement + order insert) |
| TASK-017 | order-mq | Outbox publisher (SKIP LOCKED cron, Kafka ACK) |
| TASK-018 | payment | VNPay create URL + callback handler |
| TASK-019 | employee | Timesheet (Redis BitSet, monthly bitmap) |
| TASK-020 | booking | Booking stub + demo controllers (Hi, SecureApi, /ticket/ping/java) |

**Gate**: each module has at least 1 happy-path test + 1 OOS path test + 1 parity baseline against Java golden response.

### Phase 2 — Hardening + cutover (TASK-021..022)

| Task | Title | Status |
|------|-------|--------|
| TASK-021 | parity_tests | ✅ done (2026-07-14) |
| TASK-022 | cutover | ✅ done (2026-07-14) |

**Gate**: 100% green-JSON parity on listed routes; nginx shadow traffic 24h clean; cutover plan signed off.

### Phase 3 — Frontend port (TASK-023..024)

| Task | Title | Status |
|------|-------|--------|
| TASK-023 | frontend_migrate | ✅ done (2026-07-14) |
| TASK-024 | frontend_smoke_e2e | ✅ done (2026-07-14) |

**Gate**: `npm run build` passes; docker compose up `frontend`; smoke test against running .NET API.

## Migration invariants per task

```
1. dotnet build FlashSale.slnx            → 0 error
2. dotnet test (unit + architecture)      → green
3. integration tests (where applicable)   → green
4. KHOWN_DIFFERENCES.md                   → updated if behaviour diverges
5. TASK_INDEX.md                          → row updated, commit recorded
6. Commit message                         → [TASK-XXX] slug: mô tả
```

## Parallelism

Tasks 11..20 may run sequentially (safer, easier review) but some pairs can be parallelised after their shared dependency ships:

| After | Parallel |
|-------|----------|
| TASK-004 | TASK-005 |
| TASK-005+006 | TASK-007 |
| TASK-007+008 | TASK-009+010 |
| TASK-009+010 | TASK-011/012/019/020 (different controllers, no shared file) |
| TASK-013 | TASK-014 |
| TASK-015 | TASK-016/017 |

User should sequence serially for first few tasks to learn the loop, then fan out.

## Risk register

| Risk | Mitigation |
|------|-----------|
| Lua atomic semantics drift between Spring/Redisson-Lua and StackExchange.Redis-EVAL | Write a contract test that fires 1000 concurrent CAS requests and asserts stock never goes negative (TASK-013/021). |
| VNPay HMAC-SHA512 byte ordering differs | Match Java's TreeMap ordering + URL-encoding rules exactly (TASK-018). |
| Outbox publisher racing multiple replicas | Use SELECT ... FOR UPDATE SKIP LOCKED (TASK-017). |
| Kafka consumer idempotency on rebalance | `idempotency_key` table with INSERT IGNORE inside the same transaction (TASK-016). |
| Cancellation order: DB OK but Redis fail | Log to inconsistency table; user runs reconciliation script. Note in KNOWN_DIFFERENCES. |