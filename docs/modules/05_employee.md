# Module 05 — Employee Timesheet

## Java → .NET

| Java | .NET |
|------|------|
| `com.xxxx.ddd.controller.http.EmployeeController` | `FlashSale.Api.Controllers.EmployeeController` |
| `com.xxxx.ddd.application.service.employee.cache.EmployeeCacheService` | `FlashSale.Application.Services.IEmployeeCacheService` |

## Endpoints

| Method | Route | Behaviour |
|--------|-------|-----------|
| POST | `/api/sign-in/{userId}` | Sign in today |
| POST | `/api/sign-in/{userId}/any-date?date=YYYY-MM-DD` | Sign in on specific date |
| GET | `/api/sign-in/{userId}/check?date=YYYY-MM-DD` | Has signed in on date |
| GET | `/api/sign-in/{userId}/monthly-count?month=YYYY-MM` | Total days in month |
| GET | `/api/sign-in/{userId}/monthly-sign-details?month=YYYY-MM` | Map with list of days |
| GET | `/api/sign-in/{userId}/first-day?month=YYYY-MM` | First day signed in |
| GET | `/api/sign-in/{userId}/consecutive-days?date=YYYY-MM-DD` | Consecutive days back from date |
| GET | `/api/sign-in/{userId}/summary?date=YYYY-MM-DD` | Aggregate all stats |

## Storage

Redis BitSet:
- Key: `user:sign:{userId}:{yyyyMM}`
- Bit offset: `dayOfMonth - 1` (0-based)
- 1 byte per user per month (very compact)

Operations:
- `SETBIT key offset 1` → mark signed in
- `GETBIT key offset` → check
- `BITCOUNT key` → cardinality (count of 1s)

## .NET implementation

StackExchange.Redis native operations:

```csharp
await Db.StringSetBitAsync(key, offset, true);
var hasSigned = await Db.StringGetBitAsync(key, offset);
var count = await Db.StringBitCountAsync(key);
```

For `monthly-sign-details` (list of days), iterate bit 0..30 and collect indexes where bit=1.

For `consecutive-days`, iterate backward from `dayOfMonth - 1` until a 0 is found.

## Tasks

- **TASK-019**: employee_timesheet — port all 8 endpoints (done 2026-07-14)

## Why BitSet?

- 31 bits = 4 bytes for a whole month
- O(1) for set/get
- O(N) for BITCOUNT over 31 bits (negligible)
- No TTL needed — bitmaps naturally compress repeated writes

## Known quirks

- Java uses `RBitSet` (Redisson). StackExchange.Redis uses native Redis BITSET ops (same underlying mechanism).
- Java: `int offset = date.getDayOfMonth() - 1` (0-based). .NET: same convention.
- Java returns `Map<String, Object>` for `monthly-sign-details` and `summary`. .NET uses typed DTOs (`MonthlySignDetailsDto`, `EmployeeSummaryDto`).
- Java uses JVM default timezone via `LocalDate`; .NET pins UTC to keep keys stable across a multi-pod fleet (KNOWN_DIFFERENCES §21).
- Java `getFirstSignDay` scans bits 0..30 unconditionally (would return phantom 30/31 in February); .NET scans only 0..lengthOfMonth-1 (KNOWN_DIFFERENCES §22).
- Java `getConsecutiveDays` walks back within the same month only. .NET matches — no cross-month look-up (KNOWN_DIFFERENCES §23).
- Java endpoints are open (no auth). .NET mirrors (KNOWN_DIFFERENCES §25).