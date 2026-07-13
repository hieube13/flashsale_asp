# TASK-002 — shared_contracts

| Field | Value |
|-------|-------|
| Status | ✅ done |
| Branch | — |
| Module | contracts |
| Phase | 0 — Scaffold |
| Commit | — |
| Completed | 2026-07-13 |

## Mục tiêu

Tạo các DTO request/response, message contracts, result envelope — theo đúng Java `controller/dto` + `controller/model/vo` + `infrastructure/mq`.

## Tệp Java nguồn (chỉ đọc)

- `xxxx-controller/.../controller/model/vo/ResultMessage.java` — `ResultMessage<T>`
- `xxxx-controller/.../controller/model/enums/ResultUtil.java` — `ResultUtil.data()`, `error()`
- `xxxx-controller/.../controller/dto/CreateBookingRequest.java`
- `xxxx-controller/.../controller/dto/PlaceOrderMQRequest.java`
- `xxxx-controller/.../controller/dto/CreateTicketRequest.java`, `CreateTicketFullRequest.java`, `CreateTicketDetailRequest.java`, `UpdateTicketRequest.java`
- `xxxx-application/.../application/model/command/CreateBookingCommand.java`, `CreateTicketCommand.java`, `CreateTicketDetailCommand.java`, `UpdateTicketCommand.java`
- `xxxx-application/.../application/model/TicketDTO.java`, `TicketDetailDTO.java`, `TicketOrderDTO.java`, `BookingDTO.java`, `PagedOrdersDTO.java`
- `xxxx-application/.../application/model/response/PlaceOrderResponse.java`
- `xxxx-infrastructure/.../infrastructure/mq/PlaceOrderMQMessage.java`

## File .NET đích (đã tạo)

- `src/FlashSale.Contracts/Dto/ResultMessage.cs` — `ResultMessage<T>` + `ResultMessage`
- `src/FlashSale.Contracts/Dto/PlaceOrderResponse.cs`
- `src/FlashSale.Contracts/Dto/CreateBookingRequest.cs`
- `src/FlashSale.Contracts/Dto/PlaceOrderMqRequest.cs`
- `src/FlashSale.Contracts/Dto/TicketRequests.cs` — CreateTicketFullRequest, CreateTicketRequest, CreateTicketDetailRequest, UpdateTicketRequest
- `src/FlashSale.Contracts/Dto/TicketDtos.cs` — TicketDto, TicketDetailDto, TicketOrderDto, PagedOrdersDto, BookingDto
- `src/FlashSale.Contracts/Enums/ResultCode.cs` — moved out of Domain so Contracts has no deps
- `src/FlashSale.Contracts/Messages/PlaceOrderMqMessage.cs`

## Checklist

- [x] ResultMessage<T> với `Data`, `Error`, `FromCode`
- [x] PlaceOrderResponse với `Ok`, `Failed` factories
- [x] Tất cả DTO dùng `record` (init-only properties)
- [x] camelCase JSON convention (set globally in Program.cs)
- [x] Build pass

## Verification

```powershell
dotnet build src/FlashSale.Contracts/FlashSale.Contracts.csproj
```