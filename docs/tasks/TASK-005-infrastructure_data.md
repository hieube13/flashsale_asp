# TASK-005 — infrastructure_data

| Field | Value |
|-------|-------|
| Status | ✅ done |
| Branch | — |
| Module | infra |
| Phase | 0 — Scaffold |
| Commit | — |
| Completed | 2026-07-13 |

## Mục tiêu

EF Core DbContext cho 8 entities, MySQL Pomelo provider, IDbConnectionFactory cho Dapper (sẽ dùng ở TASK-012 cho dynamic monthly tables).

## Tệp Java nguồn (chỉ đọc)

- `xxxx-infrastructure/.../infrastructure/persistence/mapper/*JPAMapper.java` (8 files) — reference cho entity shape
- `environment/mysql/init/ticket_init.sql` — schema

## File .NET đích (đã tạo)

- `src/FlashSale.Infrastructure/Data/FlashSaleDbContext.cs` — 8 DbSet, OnModelCreating mirror Java column types
- `src/FlashSale.Infrastructure/Data/IDbConnectionFactory.cs`
- `src/FlashSale.Infrastructure/Data/MySqlConnectionFactory.cs` + `MySqlOptions`
- `src/FlashSale.Infrastructure/FlashSale.Infrastructure.csproj` — added `Microsoft.EntityFrameworkCore`, `Pomelo.EntityFrameworkCore.MySql`, `MySqlConnector`

## Checklist

- [x] DbContext với 8 DbSet
- [x] Column types mirror Java (`TEXT`, `BIGINT`, `DATETIME`, `DECIMAL(16,3)`)
- [x] Default values per Java (`CURRENT_TIMESTAMP`)
- [x] Index definitions (UNIQUE on `payment_id`, `order_number`; idx `status_created` on outbox_event)
- [x] MySqlConnectionFactory uses MySqlConnector directly (for Dapper dynamic tables)
- [x] EF migration policy = `none` (DDL owned by SQL scripts, same as Java `ddl-auto: none`)

## Verification

```powershell
dotnet build src/FlashSale.Infrastructure/FlashSale.Infrastructure.csproj
```

## Implementation notes

- Entity `TicketDetail.Status` maps to table `ticket_item` (Java @Table annotation).
- OutboxEvent composite index `(status, created_at)` matches Java `idx_status_created`.
- IdempotencyKey primary key = `token` (matches Java @Id).
- `order_queue` table — `token` UNIQUE.
- `payment_transaction` — `payment_id` UNIQUE.