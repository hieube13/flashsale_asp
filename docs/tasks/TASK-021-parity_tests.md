# TASK-021 — parity_tests

| Field | Value |
|-------|-------|
| Status | 🟢 done |
| Branch | `f_task_021_parity_tests` |
| Module | testing |
| Phase | 2 — Hardening |
| Commit | — |
| Completed | 2026-07-14 |

## Mục tiêu

Golden JSON tests: every .NET endpoint must produce byte-identical (or `timestamp`-excluded) JSON to Java baseline for the same input.

## Approach

1. **Capture baseline** — once TASK-011..020 ships, run Java locally, capture responses from all 22 endpoints into `tests/FlashSale.ContractTests/Baselines/{route}.json` files.
2. **Compare** — run .NET, diff JSON, allow only `timestamp` differences (it's `System.currentTimeMillis()` vs `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`).
3. **CI gate** — parity test must pass before any TASK-022 cutover is signed off.

## Tệp Java nguồn (chỉ đọc)

- All controllers (run with curl)

## File .NET đích (sẽ tạo)

- `tests/FlashSale.ContractTests/Baselines/` — golden JSON files
- `tests/FlashSale.ContractTests/Parity/TicketParityTests.cs`
- `tests/FlashSale.ContractTests/Parity/OrderParityTests.cs`
- `tests/FlashSale.ContractTests/Parity/PaymentParityTests.cs`
- `tests/FlashSale.ContractTests/Parity/EmployeeParityTests.cs`
- `tests/FlashSale.ContractTests/Parity/BookingParityTests.cs`
- `tests/FlashSale.ContractTests/Parity/DemoParityTests.cs`
- `tests/FlashSale.ContractTests/Helpers/JsonDiff.cs` — exclude `timestamp`, deep-compare

## Acceptance criteria

- [ ] All 22 endpoints have baseline JSON in `Baselines/`
- [ ] Contract tests pass with timestamp excluded
- [ ] Document any intentional difference in KNOWN_DIFFERENCES.md
- [ ] Test failures block TASK-022 cutover

## Verification

```powershell
dotnet test tests/FlashSale.ContractTests
# All parity tests pass

# When regenerating baselines:
#   1. Run Java locally (port 1122)
#   2. Run capture script:
.\tests\FlashSale.ContractTests\Scripts\capture-baselines.ps1 -JavaBase "http://localhost:1122"
#   3. Commit baselines
```

## Suggested commit

```
[TASK-021] parity_tests: golden json comparison java vs dotnet for 22 endpoints
```