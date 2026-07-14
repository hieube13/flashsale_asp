# Module 06 — Booking (demo)

## Java → .NET

| Java | .NET |
|------|------|
| `com.xxxx.ddd.controller.http.BookingController` | `FlashSale.Api.Controllers.BookingController` |
| `com.xxxx.ddd.application.service.booking.BookingAppService` | `FlashSale.Application.Services.IBookingAppService` |
| `com.xxxx.ddd.application.service.booking.impl.BookingAppServiceImpl` | `FlashSale.Application.Services.Implementations.BookingAppServiceImpl` |

## Endpoints

| Method | Route | Behaviour |
|--------|-------|-----------|
| POST | `/api/bookings` | Create Booking (request body: `{ticketId, quantity}`) |

## Entities

- `Booking` → `booking` table
  - `id` BIGINT PK
  - `ticket_id` BIGINT
  - `quantity` INT
  - `booking_code` VARCHAR
  - `status` INT (0=PENDING, 1=CONFIRMED, 2=CANCELLED)
  - `created_at` DATETIME

## Tasks

- **TASK-020**: booking_demo — port BookingController + impl (done 2026-07-14)

## Known quirks

- Java: `BookingControllerMapper.toCommand` (MapStruct) → in .NET we use a simple manual mapping in the controller (lightweight).
- Java returns `ResultMessage<BookingDTO>` with `BookingDTO(id, ticketId, quantity, bookingCode, status)` — **no `createdAt`**. .NET `BookingDto` mirrors Java exactly (5 fields).
- Java status codes: 400 for IllegalArgumentException, 500 for general errors. Preserve. **Note**: We return HTTP 200 with `success=false, code=400/500` body, matching our cross-cutting convention (KNOWN_DIFFERENCES §4). Java itself uses `ResultMessage` which is also returned over HTTP 200.
- Java does **NOT ship a DDL** for the `booking` table — runtime `POST /api/bookings` would throw against the Java stack. .NET appends `CREATE TABLE IF NOT EXISTS booking (…)` to `environment/mysql/init/01-schema.sql` (KNOWN_DIFFERENCES §26).