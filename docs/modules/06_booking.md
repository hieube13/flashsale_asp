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

- **TASK-020**: booking_demo — port BookingController + impl

## Known quirks

- Java: `BookingControllerMapper.toCommand` (MapStruct) → in .NET we use a simple manual mapping in the controller (lightweight).
- Java returns `ResultMessage<BookingDTO>` with `BookingDTO(id, ticketId, quantity, bookingCode, status, createdAt)`.
- Java status codes: 400 for IllegalArgumentException, 500 for general errors. Preserve.