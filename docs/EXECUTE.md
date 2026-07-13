# EXECUTE — How to run one task via Agent mode

This document is the **executable contract** between you (user) and the AI Agent. Each task in `docs/tasks/TASK-XXX-slug.md` is designed to be runnable end-to-end via this single prompt.

## Quick recap of mode

| Mode | Behaviour |
|------|-----------|
| Ask mode | Read-only. Cannot create/edit files. |
| Agent mode | Full read/write/exec. Files appear on disk. |

**You must be in Agent mode for any of the prompts below to actually create files.** If you're in Ask mode, files will NOT be created.

---

## Master prompt (copy-paste each task)

Replace `[TASK-XXX]` and `[Task Name]` with the values from `docs/TASK_INDEX.md`.

```
## Nhiệm vụ: [TASK-XXX] - [Task Name]

### 1. Đọc tài liệu liên quan

Đọc theo thứ tự:
1. Task spec:           docs/tasks/TASK-XXX-[slug].md
2. Task index:          docs/TASK_INDEX.md (xem task nào đã xong)
3. Known differences:   docs/KNOWN_DIFFERENCES.md
4. Architecture:        docs/FLASH_SALE_ARCHITECTURE.md
5. Internal arch:       docs/INTERNAL_ARCHITECTURE.md
6. Module liên quan:    docs/modules/[module].md (nếu có)

### 2. Tạo branch

git checkout -b f_task_XXX_[slug]

Ví dụ: f_task_013_order_cas_slice

### 3. Thực hiện theo checklist

Đọc toàn bộ checklist trong task doc và thực hiện từng item.
Đánh dấu [x] mỗi item khi xong trong task doc.

Quy tắc đặt code (Dependency Flow: Api → Application → Infrastructure → Domain ← Contracts):

| Loại file | Vị trí |
|-----------|--------|
| Entity / Repository interface | src/FlashSale.Domain/ |
| DTO Request/Response | src/FlashSale.Contracts/ |
| EF Core DbContext, Configurations | src/FlashSale.Infrastructure/Data/ |
| Dapper-based dynamic table query | src/FlashSale.Infrastructure/Data/Dynamic/ |
| Redis service, Lua scripts | src/FlashSale.Infrastructure/Cache/ |
| Kafka producer/consumer | src/FlashSale.Infrastructure/Messaging/ |
| Application Service, Validator | src/FlashSale.Application/ |
| Controller, Middleware | src/FlashSale.Api/Controllers/ |
| BackgroundService (outbox, kafka consumer) | src/FlashSale.Api/Workers/ |
| appsettings, Program.cs | src/FlashSale.Api/ |
| Unit Test | tests/FlashSale.UnitTests/ |
| Integration Test (Testcontainers) | tests/FlashSale.IntegrationTests/ |
| Contract Test (golden JSON) | tests/FlashSale.ContractTests/ |
| Architecture Test (NetArchTest) | tests/FlashSale.ArchitectureTests/ |
| k6 scripts | tests/FlashSale.LoadTests/ |

Quy tắc khi code:
- Giữ nguyên HTTP route, method, JSON property names (camelCase giống Java)
- Giữ nguyên Redis key prefix (PRO_TICKET:..., TICKET:..., LOCK:..., TOKEN_LOCK_KEY...)
- Giữ nguyên Kafka topic name (order-place-topic) và message schema
- Giữ nguyên database schema (đọc environment/mysql/init/ticket_init.sql)
- Giữ nguyên order_number format: "OKX-SGN-{userId}-{seq}-{tsMillis}"
- Không hard-code secret/connection string — dùng appsettings + env var
- Mỗi log phải có CorrelationId (nếu task có message chain)
- Worker phải idempotent — xử lý duplicate không tạo side-effect
- Outbox publisher phải dùng SELECT ... FOR UPDATE SKIP LOCKED (multi-instance safe)

### 4. Build & verify

Sau khi checklist xong:
1. dotnet build FlashSale.slnx — phải pass không có lỗi
2. dotnet test tests/FlashSale.UnitTests/ — phải pass
3. dotnet test tests/FlashSale.ArchitectureTests/ — phải pass (kiểm tra dependency direction)
4. Nếu có integration test: dotnet test tests/FlashSale.IntegrationTests/
5. Nếu có contract test: dotnet test tests/FlashSale.ContractTests/
6. Kiểm tra không có hard-coded secret

### 5. Update documentation

Sau khi code hoàn thành:

1. docs/tasks/TASK_INDEX.md:
   - Đổi Status của TASK-XXX thành "done"
   - Điền Completed (YYYY-MM-DD), Branch, Commit (short hash)
   - Commit riêng: "docs: update TASK_INDEX [TASK-XXX]"

2. docs/FLASH_SALE_ARCHITECTURE.md:
   - Cập nhật section "Trạng thái hiện tại" nếu có project/component mới
   - Cập nhật Database Schema nếu có entity mới
   - Cập nhật API Endpoints nếu có route mới

3. docs/INTERNAL_ARCHITECTURE.md:
   - Thêm entity mới vào danh sách nếu có
   - Thêm service/worker mới vào danh sách nếu có
   - Cập nhật naming convention nếu có pattern mới

4. docs/modules/ (nếu liên quan):
   - Cập nhật file module tương ứng với thay đổi API / behavior
   - Tạo file module mới nếu task tạo module chưa có spec

5. docs/KNOWN_DIFFERENCES.md (nếu phát hiện bug/hành vi khác):
   - Thêm entry mới với: Java behavior, .NET behavior, quyết định (preserve/fix), lý do

### 6. Suggested commit (KHÔNG tự commit)

Không tự chạy git commit. Cuối mỗi section, in ra dòng lệnh để user tự chạy:

Sau section code (step 3-4):
git add src/ tests/
git commit -m "[TASK-XXX] slug: mô tả ngắn ở thì hiện tại"

Sau section docs (step 5):
git add docs/
git commit -m "docs: update TASK_INDEX [TASK-XXX]"

Format commit message: [TASK-XXX] slug: mô tả ngắn ở thì hiện tại

Ví dụ:
[TASK-001] solution_scaffold: create 5 src + 4 tests projects
[TASK-013] order_cas_slice: preserve lua atomic and manual redis compensation

### 7. Checklist hoàn thành

Trước khi kết thúc task, verify:
- [ ] dotnet build FlashSale.slnx pass không có warning nghiêm trọng
- [ ] dotnet test pass (unit + architecture)
- [ ] Không hard-coded secret / connection string
- [ ] Nếu có Worker: idempotent và có Ack/Nack rõ ràng
- [ ] Nếu có Message chain: CorrelationId được truyền đúng
- [ ] TASK_INDEX.md đã cập nhật (status=done, commit filled)
- [ ] Architecture / Internal arch đã cập nhật (nếu có thay đổi)
- [ ] KNOWN_DIFFERENCES.md đã cập nhật (nếu phát hiện bug)
- [ ] Commit message đúng format
- [ ] Đã in suggested commit command cho user — KHÔNG tự commit
```

---

## Task Flow (one shot)

```
User (you)                              AI Agent (Cursor)
  │                                          │
  │  copy prompt, paste into Agent mode      │
  ├─────────────────────────────────────────►│
  │                                          │
  │                                          ├─ read docs/tasks/TASK-XXX.md
  │                                          ├─ git checkout -b f_task_XXX_slug
  │                                          ├─ read Java source(s) at paths
  │                                          │  given in task doc
  │                                          ├─ write .NET code at target paths
  │                                          ├─ dotnet build FlashSale.slnx
  │                                          ├─ dotnet test (unit + arch)
  │                                          ├─ update TASK_INDEX.md
  │                                          ├─ in suggested commit commands
  │                                          │
  │   ← suggested commit commands ──────────┤
  │                                          │
  │  user reviews, then:                     │
  │  git add ... && git commit -m "..."      │
  │                                          │
  ▼                                          ▼
next task: copy next prompt
```

---

## Mapping reference (Agent uses this)

### Java package → .NET namespace

| Java | .NET |
|------|------|
| `com.xxxx.ddd.controller.http` | `FlashSale.Api.Controllers` |
| `com.xxxx.ddd.controller.dto` | `FlashSale.Contracts.Dto` |
| `com.xxxx.ddd.application.service` | `FlashSale.Application.Services` |
| `com.xxxx.ddd.application.cronjob` | `FlashSale.Api.Workers` |
| `com.xxxx.ddd.domain.model.entity` | `FlashSale.Domain.Entities` |
| `com.xxxx.ddd.domain.respository` | `FlashSale.Domain.Repositories` |
| `com.xxxx.ddd.domain.service` | `FlashSale.Domain.Services` |
| `com.xxxx.ddd.infrastructure.persistence.mapper` | `FlashSale.Infrastructure.Data.Configurations` |
| `com.xxxx.ddd.infrastructure.cache` | `FlashSale.Infrastructure.Cache` |
| `com.xxxx.ddd.infrastructure.mq` | `FlashSale.Infrastructure.Messaging` |
| `com.xxxx.ddd.infrastructure.gateway` | `FlashSale.Infrastructure.External` |
| `com.xxxx.ddd.infrastructure.distributed` | `FlashSale.Infrastructure.DistributedLock` |

### HTTP routes that must stay identical

| Java (port 1122) | .NET (port 5080) |
|------------------|------------------|
| GET `/ticket/active` | GET `/ticket/active` |
| POST `/ticket/create` | POST `/ticket/create` |
| GET `/ticket/{id}` | GET `/ticket/{id}` |
| PUT `/ticket/{id}` | PUT `/ticket/{id}` |
| PUT `/ticket/{id}/active` | PUT `/ticket/{id}/active` |
| PUT `/ticket/{id}/inactive` | PUT `/ticket/{id}/inactive` |
| DELETE `/ticket/{id}` | DELETE `/ticket/{id}` |
| GET `/ticket/{ticketId}/detail/{detailId}` | GET `/ticket/{ticketId}/detail/{detailId}` |
| GET `/ticket/{ticketId}/detail/{detailId}/order` | GET `/ticket/{ticketId}/detail/{detailId}/order` |
| GET `/ticket/ping/java` | GET `/ticket/ping/java` |
| POST `/order/cas` | POST `/order/cas` |
| GET `/order/{ticketId}/{quantity}/order` | GET `/order/{ticketId}/{quantity}/order` |
| GET `/order/{ticketId}/{quantity}/cas` | GET `/order/{ticketId}/{quantity}/cas` |
| GET `/order/{ticketId}/{quantity}/{userId}/queued` | GET `/order/{ticketId}/{quantity}/{userId}/queued` |
| GET `/order/{userId}/list` | GET `/order/{userId}/list` |
| GET `/order/{userId}/list/page` | GET `/order/{userId}/list/page` |
| GET `/order/{userId}/{orderNumber}` | GET `/order/{userId}/{orderNumber}` |
| PUT `/order/{userId}/{orderNumber}/cancel` | PUT `/order/{userId}/{orderNumber}/cancel` |
| POST `/order/mq` | POST `/order/mq` |
| GET `/order/mq/status/{token}` | GET `/order/mq/status/{token}` |
| POST `/payment/create` | POST `/payment/create` |
| POST `/api/bookings` | POST `/api/bookings` |
| POST `/api/sign-in/{userId}` | POST `/api/sign-in/{userId}` |
| GET `/hello/hi` | GET `/hello/hi` |
| POST `/api/v1/secure/data` | POST `/api/v1/secure/data` |

### Java annotation → .NET pattern

| Java | .NET |
|------|------|
| `@SpringBootApplication` | `WebApplication.CreateBuilder()` + DI manual |
| `@RestController` | `[ApiController]` |
| `@GetMapping/@PostMapping/...` | `[HttpGet]/[HttpPost]/...` |
| `@RequestBody @Valid` | `[FromBody]` + FluentValidation |
| `@Autowired` | Constructor injection (primary ctor or manual) |
| `@Service` / `@Component` | DI registration in Program.cs |
| `@Transactional` | `IDbContextTransaction` explicit or `TransactionScope` |
| `@Scheduled(fixedDelay=1000)` | `BackgroundService` + `PeriodicTimer` |
| `@KafkaListener(topics=..., groupId=..., concurrency=...)` | `Confluent.Kafka` `IConsumer` in `BackgroundService` |
| `@CircuitBreaker(name=...)` | Polly `ResiliencePipeline` |
| `@RateLimiter(name=...)` | ASP.NET Core 8 built-in `RateLimiter` |
| `@Lombok @Data/@Builder` | C# `record` or class with init-only properties |
| `@Slf4j` | `ILogger<T>` injection |

### Config mapping

| Java (application.yml) | .NET (appsettings.json) |
|------------------------|-------------------------|
| `server.port: 1122` | `"Urls": "http://*:5080"` |
| `spring.datasource.url` | `"ConnectionStrings:MySql"` |
| `spring.data.redis.host/port` | `"Redis:ConnectionString"` |
| `spring.kafka.bootstrap-servers` | `"Kafka:BootstrapServers"` |
| `resilience4j.circuitbreaker.instances.*` | Polly v8 `ResiliencePipeline` |
| `management.endpoints.web.exposure.include: '*'` | ASP.NET HealthChecks + `prometheus-net` |

---

## When AI Agent fails

### Build error
1. Read error message.
2. Inspect failing `.cs`.
3. Fix per C# 12 conventions.
4. Re-build.

### Test failure
1. Read test output.
2. Identify which assertion failed.
3. Compare to Java behaviour (re-read referenced Java file).
4. Decide: fix .NET code, fix test, or record in KNOWN_DIFFERENCES.

### Architecture test fails (dependency direction wrong)
1. Check project reference.
2. Enforce: Api → Application → Infrastructure → Domain ← Contracts.
3. Domain must NOT reference Application/Infrastructure/Api.
4. Infrastructure must NOT reference Api.

### Bug found in Java
1. Add entry to `KNOWN_DIFFERENCES.md`.
2. In .NET: preserve (keep bug for parity) or fix (record in entry).
3. User reviews the preserve/fix decision at PR time.

---

## Don't

- Don't let AI Agent commit. User commits.
- Don't change DB schema without flagging in KNOWN_DIFFERENCES.
- Don't change Redis key prefix — breaks parity.
- Don't change Kafka topic name — breaks consumers.
- Don't add a NuGet package not listed in the task doc without asking first.
- Don't change JSON property names — breaks frontend.

## Do

- Run `dotnet build` after every meaningful change.
- Compare to Java golden response (curl both, diff).
- Update TASK_INDEX.md immediately after each task.
- Read KNOWN_DIFFERENCES.md before starting any task.