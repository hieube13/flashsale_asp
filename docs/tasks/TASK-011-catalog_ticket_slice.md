# TASK-011 — catalog_ticket_slice

| Field | Value |
|-------|-------|
| Status | 🟡 pending |
| Branch | `f_task_011_catalog_ticket_slice` |
| Module | catalog |
| Phase | 1 — Feature port |
| Commit | — |
| Completed | — |

## Mục tiêu

Port `TicketController` + `TicketAppServiceImpl` + `TicketDetailController` + `TicketDetailAppServiceImpl` từ Java. Bao gồm:

- CRUD ticket (active/inactive, soft delete)
- Create ticket full (kèm TicketDetail con)
- L1 cache (Redis) cho `TicketDetail.getTicketDetailById`
- Cache warmup qua `WarmupDataBeforeEvent` cron

## Tệp Java nguồn (chỉ đọc)

- `xxxx-controller/.../controller/http/TicketController.java` — 7 endpoints
- `xxxx-controller/.../controller/http/TicketDetailController.java` — 2 endpoints + ping
- `xxxx-application/.../application/service/ticket/TicketAppService.java` + `TicketAppServiceImpl.java`
- `xxxx-application/.../application/service/ticket/TicketDetailAppService.java` + `TicketDetailAppServiceImpl.java`
- `xxxx-application/.../application/service/ticket/cache/TicketDetailCacheService.java` + `TicketDetailCacheServiceRefactor.java`
- `xxxx-application/.../application/cronjob/WarmupDataBeforeEvent.java`
- `xxxx-application/.../application/mapper/TicketMapper.java`, `TicketDetailMapper.java`
- `xxxx-controller/.../controller/mapper/TicketControllerMapper.java`

## File .NET đích (sẽ tạo)

- `src/FlashSale.Application/Services/Implementations/TicketAppServiceImpl.cs`
- `src/FlashSale.Application/Services/Implementations/TicketDetailAppServiceImpl.cs`
- `src/FlashSale.Application/Mappers/TicketMapper.cs`
- `src/FlashSale.Infrastructure/Cache/TicketDetailCacheService.cs`
- `src/FlashSale.Infrastructure/Persistence/Repositories/TicketRepositoryImpl.cs`
- `src/FlashSale.Infrastructure/Persistence/Repositories/TicketDetailRepositoryImpl.cs`
- `src/FlashSale.Api/Controllers/TicketController.cs`
- `src/FlashSale.Api/Controllers/TicketDetailController.cs`
- `src/FlashSale.Api/Workers/WarmupDataWorker.cs` (BackgroundService — daily cron)

## Endpoints to mirror

| Method | Route | Behaviour |
|--------|-------|-----------|
| GET | `/ticket/active` | All tickets where status=1 |
| POST | `/ticket/create` | Insert ticket + ticket_item in 1 tx |
| GET | `/ticket/{id}` | Detail |
| PUT | `/ticket/{id}` | Update (currently no-op in Java) |
| PUT | `/ticket/{id}/active` | Set status=1 |
| PUT | `/ticket/{id}/inactive` | Set status=0 |
| DELETE | `/ticket/{id}` | Soft delete (status=2) |
| GET | `/ticket/{ticketId}/detail/{detailId}` | With version param for optimistic lock |
| GET | `/ticket/{ticketId}/detail/{detailId}/order` | Decrement by 1 |
| GET | `/ticket/ping/java` | Sleep 1s, return OK |

## Acceptance criteria

- [ ] All 10 endpoints return correct JSON (compare vs Java curl output)
- [ ] `/ticket/create` inserts in single transaction (rollback on failure)
- [ ] `/ticket/{ticketId}/detail/{detailId}` reads L1 cache first, falls back to DB
- [ ] WarmupDataWorker populates Redis with active tickets on startup + daily 00:00
- [ ] Unit tests for TicketAppServiceImpl (mock ITicketRepository + ITicketDetailCacheService)
- [ ] Integration tests against Testcontainers MySQL + Redis
- [ ] Architecture tests still pass
- [ ] Update TASK_INDEX.md, FLASH_SALE_ARCHITECTURE.md (API Endpoints table), INTERNAL_ARCHITECTURE.md (entity/service list)

## Verification

```powershell
dotnet build FlashSale.slnx
dotnet test tests/FlashSale.UnitTests --filter "FullyQualifiedName~Ticket"
dotnet test tests/FlashSale.IntegrationTests --filter "FullyQualifiedName~Ticket"
dotnet test tests/FlashSale.ArchitectureTests

# Smoke test
curl http://localhost:5080/ticket/active
curl -X POST http://localhost:5080/ticket/create -H "Content-Type: application/json" -d '{"ticket":{"name":"Test","description":"x","startTime":"2026-12-12T00:00:00","endTime":"2026-12-12T23:59:59"},"detail":{"name":"VIP","stockInitial":100,"stockAvailable":100,"priceOriginal":500000}}'
```

## Suggested commit

```
[TASK-011] catalog_ticket_slice: port ticket crud + l1 cache + warmup cron
```