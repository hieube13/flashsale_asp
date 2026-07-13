# TASK-019 — employee_timesheet

| Field | Value |
|-------|-------|
| Status | 🟡 pending |
| Branch | `f_task_019_employee_timesheet` |
| Module | employee |
| Phase | 1 — Feature port |
| Commit | — |
| Completed | — |

## Mục tiêu

Port Employee timesheet dùng Redis BitSet. Monthly attendance bitmap.

## Tệp Java nguồn (chỉ đọc)

- `xxxx-controller/.../controller/http/EmployeeController.java` — 8 endpoints
- `xxxx-application/.../application/service/employee/cache/EmployeeCacheService.java` — BitSet operations

## File .NET đích (sẽ tạo)

- `src/FlashSale.Application/Services/Implementations/EmployeeCacheServiceImpl.cs` — use `IDatabase.StringSetBitAsync` / `StringGetBitAsync`
- `src/FlashSale.Api/Controllers/EmployeeController.cs`

## Redis BitSet in StackExchange.Redis

```csharp
// Set bit
await Db.StringSetBitAsync("user:sign:10001:202504", dayOffset, true);

// Get bit
var hasSigned = await Db.StringGetBitAsync("user:sign:10001:202504", dayOffset);

// Cardinality (count of 1s)
var count = await Db.StringBitCountAsync("user:sign:10001:202504");
```

No Redisson equivalent — StackExchange.Redis native BITCOUNT / SETBIT / GETBIT is enough.

## Endpoints

| Method | Route | Behaviour |
|--------|-------|-----------|
| POST | `/api/sign-in/{userId}` | Sign in today |
| POST | `/api/sign-in/{userId}/any-date?date=YYYY-MM-DD` | Sign in on specific date |
| GET | `/api/sign-in/{userId}/check?date=YYYY-MM-DD` | Has signed in on date |
| GET | `/api/sign-in/{userId}/monthly-count?month=YYYY-MM` | Cardinality of month |
| GET | `/api/sign-in/{userId}/monthly-sign-details?month=YYYY-MM` | Map with days list |
| GET | `/api/sign-in/{userId}/first-day?month=YYYY-MM` | First day signed in |
| GET | `/api/sign-in/{userId}/consecutive-days?date=YYYY-MM-DD` | Consecutive days from date backwards |
| GET | `/api/sign-in/{userId}/summary?date=YYYY-MM-DD` | Aggregate all stats |

## Acceptance criteria

- [ ] BitSet operations match Java RBitSet semantics
- [ ] Key format `user:sign:{userId}:{yyyyMM}` matches Java
- [ ] month parsing: `YYYY-MM` → first day of month
- [ ] Unit tests for all 8 endpoints
- [ ] Integration test against Testcontainers Redis

## Verification

```powershell
dotnet build FlashSale.slnx
dotnet test tests/FlashSale.UnitTests --filter "FullyQualifiedName~Employee"
dotnet test tests/FlashSale.IntegrationTests --filter "FullyQualifiedName~Employee"

# Smoke
curl -X POST http://localhost:5080/api/sign-in/10001
curl "http://localhost:5080/api/sign-in/10001/monthly-count?month=2025-04"
curl "http://localhost:5080/api/sign-in/10001/summary?date=2025-04-15"
```

## Suggested commit

```
[TASK-019] employee_timesheet: stackexchange redis bitset for monthly attendance
```