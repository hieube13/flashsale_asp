# TASK-001 — solution_scaffold

| Field | Value |
|-------|-------|
| Status | ✅ done |
| Branch | — |
| Module | infra |
| Phase | 0 — Scaffold |
| Commit | — |
| Completed | 2026-07-13 |

## Mục tiêu

Tạo solution `FlashSale.slnx` với 5 src projects + 4 test projects, dependency direction đúng (Api → Application → Infrastructure → Domain ← Contracts), build xanh.

## Tệp Java nguồn (chỉ đọc, không sửa)

Không có — phase scaffold, chưa đụng logic Java.

## File .NET đích (đã tạo)

- `FlashSale.slnx`
- `src/FlashSale.Domain/FlashSale.Domain.csproj`
- `src/FlashSale.Contracts/FlashSale.Contracts.csproj`
- `src/FlashSale.Application/FlashSale.Application.csproj`
- `src/FlashSale.Infrastructure/FlashSale.Infrastructure.csproj`
- `src/FlashSale.Api/FlashSale.Api.csproj`
- `tests/FlashSale.UnitTests/FlashSale.UnitTests.csproj`
- `tests/FlashSale.IntegrationTests/FlashSale.IntegrationTests.csproj`
- `tests/FlashSale.ContractTests/FlashSale.ContractTests.csproj`
- `tests/FlashSale.ArchitectureTests/FlashSale.ArchitectureTests.csproj`
- `tests/FlashSale.LoadTests/` (folder only — k6 scripts land here)

## Checklist

- [x] dotnet new sln -n FlashSale
- [x] Tạo 5 src projects (Domain, Contracts, Application, Infrastructure, Api)
- [x] Tạo 4 test projects (Unit, Integration, Contract, Architecture)
- [x] dotnet sln add (tất cả projects)
- [x] Reference graph: Api → Application → Infrastructure → Domain ← Contracts
- [x] dotnet build FlashSale.slnx → 0 error, 0 warning
- [x] Đẩy README.md, .gitignore, .dockerignore, .env.example

## Verification

```powershell
dotnet build FlashSale.slnx
# Build succeeded. 0 Warning(s) 0 Error(s)
```

## Commit

```
[TASK-001] solution_scaffold: create 5 src + 4 test projects with correct dependency direction
```

Đã bao gồm trong scaffold commit gốc.