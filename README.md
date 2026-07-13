# FlashSale — ASP.NET Core 8 migration of xxxx.com (Java Spring Boot DDD)

This repo is the .NET 8 port of `xxxx.com-18-06-26` (Spring Boot 3.3.5 / Java 21 / DDD modular monolith / port 1122).
The Java app remains the source of truth during cutover; FlashSale runs in parallel on **port 5080**.

## Quick start (local dev)

```powershell
# 1. Start MySQL + Redis + Kafka
docker compose up -d mysql redis kafka

# 2. Build + run API on port 5080
dotnet build FlashSale.slnx
dotnet run --project src/FlashSale.Api --launch-profile http

# 3. Smoke test
curl http://localhost:5080/health
curl http://localhost:5080/ticket/active
```

## Solution layout

```
FlashSale.slnx
src/
├── FlashSale.Domain          # Entities, repository interfaces, domain enums (no dependencies)
├── FlashSale.Contracts       # DTOs (request/response), Kafka messages, shared enums
├── FlashSale.Application     # Service interfaces, validators (depends on Domain + Contracts)
├── FlashSale.Infrastructure  # EF Core, Dapper, Redis, Kafka, distributed lock (depends on Application + Domain)
└── FlashSale.Api             # Controllers, workers, Program.cs, DI wiring, Serilog
tests/
├── FlashSale.UnitTests          # Pure logic tests (xUnit)
├── FlashSale.IntegrationTests   # Testcontainers MySQL/Redis/Kafka + WebApplicationFactory
├── FlashSale.ContractTests      # Golden-JSON parity vs Java baseline
├── FlashSale.ArchitectureTests  # NetArchTest — enforce dependency direction
└── FlashSale.LoadTests          # k6 scripts
docs/
├── TIMELINE.md             # Migration roadmap & phase gates
├── TASK_INDEX.md           # All tasks, status, branch, commit
├── EXECUTE.md              # How to execute each task via Agent mode
├── KNOWN_DIFFERENCES.md    # Java vs .NET behaviour deltas (preserve / fix)
├── FLASH_SALE_ARCHITECTURE.md
├── INTERNAL_ARCHITECTURE.md
├── modules/                # Per-module reference docs (mirrors xxxx.com modules)
└── tasks/                  # TASK-XXX-slug.md for each task
```

## Java → .NET dependency direction

```
Java:    controller → application → domain ← infrastructure
.NET:    Api → Application → Infrastructure → Domain ← Contracts
```

`Domain` references nothing. `Contracts` references nothing. `Application` references Domain + Contracts only.
`Infrastructure` references Application + Domain + Contracts. `Api` references everything.

This is enforced by `tests/FlashSale.ArchitectureTests` (NetArchTest).

## Migration map

See [docs/TIMELINE.md](docs/TIMELINE.md) for the 22-task plan and [docs/EXECUTE.md](docs/EXECUTE.md) for how to execute one task at a time.

Java endpoints stay byte-for-byte identical on the .NET side — see [docs/INTERNAL_ARCHITECTURE.md](docs/INTERNAL_ARCHITECTURE.md) §API parity.

## Configuration

All secrets live in `appsettings.json` (template) and `.env` (your override). Copy `.env.example` to `.env` and edit.

| Setting | Default | Notes |
|---------|---------|-------|
| Port | 5080 | Java uses 1122 |
| MySQL | localhost:3316, vetautet | Schema lives in `environment/mysql/init/ticket_init.sql` |
| Redis | localhost:6319 | Cluster mode supported later |
| Kafka | localhost:9094 | KRaft single-node (Java uses same) |

## How to contribute a task

1. Open `docs/tasks/TASK-XXX-slug.md`.
2. Copy the prompt from `docs/EXECUTE.md`, replace `[TASK-XXX]`.
3. Paste into Cursor Agent mode.
4. Agent reads Java sources referenced in the task → writes `.NET` → builds → runs tests → prints suggested commit commands.
5. You run the commits. Don't let the agent auto-commit.

## Status

See [docs/TASK_INDEX.md](docs/TASK_INDEX.md) for the live list of completed / pending tasks.