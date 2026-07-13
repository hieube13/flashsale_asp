# TASK-006 — infrastructure_redis

| Field | Value |
|-------|-------|
| Status | ✅ done (stub) |
| Branch | — |
| Module | infra |
| Phase | 0 — Scaffold |
| Commit | — |
| Completed | 2026-07-13 |

## Mục tiêu

Abstraction + concrete cho Redis. StockOrderCacheService đặt nền cho TASK-013 (Lua atomic decrement thực sự được thêm vào đây).

## Tệp Java nguồn (chỉ đọc)

- `xxxx-infrastructure/.../infrastructure/cache/redis/RedisInfrasService.java`
- `xxxx-infrastructure/.../infrastructure/cache/redis/RedisInfrasServiceImpl.java`
- `xxxx-application/.../application/service/order/cache/StockOrderCacheService.java` (Lua decrement)
- `environment/cluster-redis/` (Java reference)

## File .NET đích (đã tạo / sẽ tạo)

- `src/FlashSale.Infrastructure/Cache/IRedisInfrasService.cs` — abstraction
- `src/FlashSale.Infrastructure/Cache/RedisInfrasService.cs` — StackExchange.Redis concrete
- `src/FlashSale.Infrastructure/Cache/IStockOrderCacheService.cs` — Lua atomic contract
- `src/FlashSale.Infrastructure/Cache/StockOrderCacheService.cs` — concrete (currently using INCR/DECR; Lua script in TASK-013)

## Lua scripts to embed in TASK-013

The Redis-side atomic stock decrement is a Lua script. Reference Java behaviour:

```lua
-- KEYS[1] = "PRO_TICKET:{id}:stock_available"
-- ARGV[1] = quantity
local stock = redis.call('GET', KEYS[1])
if stock == false then return -1 end
if tonumber(stock) < tonumber(ARGV[1]) then return 0 end
redis.call('DECRBY', KEYS[1], ARGV[1])
return 1
```

Return codes: `-1` = cache miss, `0` = insufficient, `1` = success.

## Checklist

- [x] IRedisInfrasService surface mirrors Java RedisInfrasService
- [x] Concrete impl uses `IDatabase` from StackExchange.Redis
- [x] JSON serialisation via System.Text.Json (camelCase)
- [x] StockOrderCacheService stub works for early tasks
- [ ] **Lua atomic script** — added in TASK-013
- [ ] **AddStockAvailableToCacheAsync** — actual cache warm from DB in TASK-013
- [ ] **GetEffectivePriceAsync** — actual price lookup from cache in TASK-013

## Verification

```powershell
dotnet build src/FlashSale.Infrastructure/FlashSale.Infrastructure.csproj
```