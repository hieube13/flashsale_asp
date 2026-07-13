# TASK-024 — frontend_smoke_e2e

| Field | Value |
|-------|-------|
| Status | pending |
| Branch | `f_task_024_frontend_smoke_e2e` |
| Module | frontend |
| Phase | 3 — Frontend verification (chạy sau TASK-023 + tất cả TASK-011..020) |
| Commit | — |
| Completed | — |

## Mục tiêu

End-to-end smoke test: chạy đồng thời `flashsale-backend` (.NET Api :5080)
và `flashsale-frontend` (Vite dev hoặc nginx :5173), thực thi 10 user-facing
endpoints qua UI flow, đảm bảo parity với Java backend và UX không regress.

## Điều kiện tiên quyết

- TASK-011, 012, 013, 014 done (catalog + order CAS + cancel)
- TASK-018 done (payment VNPay stub OK để call `/api/bookings`)
- TASK-023 done (frontend running at `:5173`)
- `docker compose up -d mysql redis kafka` (infrastructure từ TASK-003)

## Test matrix (10 endpoints)

| # | Flow | Endpoint .NET | FE route | Expected |
|---|------|---------------|----------|----------|
| 1 | List active tickets | `GET /ticket/active` | `/` (Hero + TicketListing) | 200, `result` array ≥ 0 |
| 2 | List all tickets (filter) | `GET /ticket/active` | `/tickets` | 200, full list |
| 3 | Get ticket detail | `GET /ticket/{id}` | `/ticket/{id}` | 200, `result` có `name/priceFlash/priceOriginal` |
| 4 | Cart preview (state-only, no API) | — | `/cart` | render OK, route guard OK |
| 5 | Place order (CAS) | `POST /order/cas` | CartPage → click "Xác nhận thanh toán" | 200, `result.success=true`, có `placeOrderTaskId` |
| 6 | Booking success page | (route từ state) | `/booking-success` | render `bookingCode` |
| 7 | Manager → create event | `POST /ticket/create` | `/system/manager` → tab "Tạo sự kiện" | 200, refresh list thấy event mới |
| 8 | Manager → orders V1 | `GET /order/1/list?ntable=202605` | tab "Đơn hàng V1" | 200, table render với status badge |
| 9 | Manager → orders V2 (cursor) | `GET /order/1/list/page?ntable=202605&cursor=0&limit=50` | tab "�ơn hàng V2" | 200, `result.items`, `hasMore`, button "Tải thêm" |
| 10 | Manager → cancel order | `PUT /order/{userId}/{orderNumber}/cancel` | click "Hủy" trên order có `orderStatus===0` | 200, reload list thấy status=2 (Đã hủy) |

## Procedure

### 1. Khởi động stack

```powershell
cd F:\TipJavascript\Microservice\flashsale
docker compose up -d mysql redis kafka prometheus grafana

# Backend
dotnet run --project src/FlashSale.Api --urls=http://localhost:5080 &

# Frontend
cd frontend
npm install
npm run dev &
# đợi ~3s cho Vite ready
```

### 2. Health check

```powershell
curl http://localhost:5080/health
curl http://localhost:5080/ticket/active
curl http://localhost:5173  # FE HTML root
```

### 3. Manual smoke (browser)

| Bước | Action | Verify |
|------|--------|--------|
| 1 | Mở `http://localhost:5173` | Hero + TicketListing load, có ≥1 ticket active |
| 2 | Click vào 1 ticket | navigate `/ticket/{id}`, hiển thị giá + mô tả |
| 3 | Chọn quantity > 0 → click "Đặt vé" | navigate `/cart`, hiển thị tóm tắt |
| 4 | Click "Xác nhận thanh toán" | spinner → navigate `/booking-success` với bookingCode |
| 5 | Mở `http://localhost:5173/system/manager` | 4 tabs render, default = "Tạo sự kiện" |
| 6 | Submit form "Tạo sự kiện" | toast "Tạo sự kiện thành công!" + form reset |
| 7 | Tab "Đơn hàng V1" → nhập `202605` → "Tải lại" | table render, hiển thị order từ #4 |
| 8 | Click "Hủy" trên order có status 0 | toast "Hủy đơn thành công", reload thấy status=2 |
| 9 | Tab "Đơn hàng V2" → "Tải thêm 50 đơn" nếu hasMore | page append 50 rows mới, cursor update |
| 10 | Tab "Danh sách vé" → click "Ngừng" | toast xác nhận, row đổi badge "Tạm ngừng" |

### 4. Automation (Playwright — optional, Phase sau)

Nếu có thời gian, viết `tests/FlashSale.E2ETests/` với Playwright + NUnit:
- `Tests/HomePageTests.cs` — flow #1, #2, #3
- `Tests/CartAndBookingTests.cs` — flow #4, #5, #6
- `Tests/ManagerCRUDTests.cs` — flow #7-10

Cấu hình:
```csharp
// FlashSale.E2ETests.csproj
<PackageReference Include="Microsoft.Playwright" Version="1.49.0" />
```

Base URL config:
```csharp
private const string FE_BASE  = "http://localhost:5173";
private const string API_BASE = "http://localhost:5080";
```

## Acceptance criteria

- [ ] Tất cả 10 flow pass trong manual smoke
- [ ] Không có 4xx/5xx trong Network tab khi click happy path
- [ ] Booking flow (`/cart` → `/booking-success`) hiển thị `bookingCode` đúng
- [ ] Manager "Tạo sự kiện" thực sự tạo DB row (verify qua `GET /ticket/active`)
- [ ] Cancel order đổi `orderStatus` từ 0 → 2 trong cả UI và API response
- [ ] FE không có console error/warning (mở DevTools → Console)
- [ ] i18n (`vi-VN`) hiển thị đúng cho tất cả 14 page
- [ ] Không có regression so với Java: ticket listing, search, filter, sort hoạt động như cũ

## Verification

```powershell
# Sau khi manual smoke pass:
echo "ALL FLOWS PASS" | Out-File artifacts/frontend-smoke.log
git add docs/tasks/TASK-024-frontend_smoke_e2e.md
git commit -m "docs(TASK-024): record frontend E2E smoke result"
```

## Suggested commit

```
[TASK-024] frontend_smoke_e2e: verified 10 user-facing flows FE ↔ .NET Api, parity with Java preserved
```

## Rollback plan

Nếu smoke fail:
1. Check FE console → report bug trong `KNOWN_DIFFERENCES.md`
2. Nếu backend thiếu endpoint → defer FE task, return to backend task tương ứng
3. Nếu CORS → bật `AddCors()` trong `Program.cs` (nhưng nên dùng proxy thay)
