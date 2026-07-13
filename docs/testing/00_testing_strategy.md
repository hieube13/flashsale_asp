# Testing strategy

## Test pyramid

```
        /\
       /  \         k6 LoadTests (TASK-022)
      / 50 \        — 1000 RPS, p99 < 200ms
     /______\
    /        \
   / Contract \   ContractTests (TASK-021)
  /   Tests    \  — Golden JSON vs Java
 /______________\
/                \
/ IntegrationTests \  Testcontainers MySQL/Redis/Kafka + WebApplicationFactory
/____________________\
|                    |
|    UnitTests       |  xUnit + FluentAssertions + NSubstitute/Moq
|                    |
|____________________|
```

## Per-test-type policy

### UnitTests (`tests/FlashSale.UnitTests`)

- Target: pure logic, repository wrappers, service orchestration without IO.
- Mock: `ITicketRepository`, `ITicketDetailRepository`, `IRedisInfrasService`, `IStockOrderCacheService`.
- Libraries: xUnit, FluentAssertions, Moq (or NSubstitute).
- No DB, no Redis, no Kafka.
- `dotnet test tests/FlashSale.UnitTests` — fast (~seconds), runs on every commit.

### IntegrationTests (`tests/FlashSale.IntegrationTests`)

- Target: full slice from HTTP in to DB out via Testcontainers.
- Harness: `WebApplicationFactory<Program>` + Testcontainers.
- Each test class spins: MySQL 8, Redis 7, Kafka 3.7 (KRaft).
- Run schema migrations before tests (use `ticket_init.sql`).
- Use `Respawn` or simple `TRUNCATE` between tests.
- `dotnet test tests/FlashSale.IntegrationTests` — slower (~30s/setup), runs in CI.

### ContractTests (`tests/FlashSale.ContractTests`)

- Target: byte-identical JSON vs Java baseline.
- Baselines live in `tests/FlashSale.ContractTests/Baselines/*.json`.
- Diff allows only `timestamp` field changes.
- Run against Java first to capture baselines (one-off), then test .NET produces same.
- Added in TASK-021.

### ArchitectureTests (`tests/FlashSale.ArchitectureTests`)

- Target: dependency direction (Domain → nothing, Application → Domain/Contracts only, etc.).
- Library: NetArchTest.
- Runs on every commit, blocks PRs.

### LoadTests (`tests/FlashSale.LoadTests/`)

- k6 scripts (no .NET project).
- Adapted from Java `benchmark/k6/flash-sale.js`.
- Targets: 1000 RPS sustained, p99 latency, error rate < 0.1%.
- Ran in TASK-022 against cutover cluster.

## Conventions

- Test naming: `MethodName_StateUnderTest_ExpectedBehavior`
- Arrange-Act-Assert pattern (no library; just comments / blank lines)
- One assertion per test (preferred) — split if multiple concerns
- Mock via constructor injection, NOT via service locator
- Test data builder pattern for complex entities

## Test commands

```powershell
# Unit only (fast, ~5s)
dotnet test tests/FlashSale.UnitTests

# Architecture (fast, ~2s)
dotnet test tests/FlashSale.ArchitectureTests

# Integration (~1-2min including container spinup)
dotnet test tests/FlashSale.IntegrationTests

# Contract (vs Java golden)
dotnet test tests/FlashSale.ContractTests

# All
dotnet test FlashSale.slnx
```

## Coverage targets

| Module | Unit | Integration | Contract | Architecture |
|--------|------|-------------|----------|--------------|
| Catalog | 80% | 1 happy + 1 not-found | ✅ | ✅ |
| Order CAS | 80% | concurrent 1000-request test | ✅ | ✅ |
| Order Cancel | 80% | lock contention test | ✅ | ✅ |
| OrderMQ producer | 80% | tx rollback path | ✅ | ✅ |
| OrderMQ consumer | 80% | idempotency replay | ✅ | ✅ |
| OrderMQ publisher | 80% | skip-locked concurrency | n/a | ✅ |
| Payment | 80% | URL byte-equality vs Java | ✅ | ✅ |
| Employee | 80% | bitset ops | ✅ | ✅ |
| Booking | 80% | basic CRUD | ✅ | ✅ |
| Demo | 60% | rate-limit threshold | ✅ | ✅ |

## Future improvements (TASK-022 cutover)

- Wire xUnit results to Azure DevOps / GitHub Actions
- Publish coverage reports to Coveralls or SonarQube
- Mutation testing with Stryker.NET (spot-check critical paths)
- Property-based testing with FsCheck for order-number format correctness