# Modules — Index

Reference docs per module in `xxxx.com-18-06-26` (Java) → `flashsale` (.NET 8).

Each module file documents:
- Java package & key files
- .NET namespace & equivalent files
- Endpoints (HTTP)
- Domain entities touched
- Cache / lock / Kafka key prefixes
- Linked tasks (which TASK-XXX ships the .NET code)

| # | Module | Java root | .NET namespace | Tasks |
|---|--------|-----------|----------------|-------|
| 01 | catalog | `controller.http.TicketController` + `TicketDetailController` | `FlashSale.Api.Controllers.Ticket*` | TASK-011, 012 |
| 02 | order | `TicketOrderAppServiceImpl.placeOrderCAS` + `decreaseStockLevel3CAS` | `FlashSale.Application.Services.TicketOrderAppService` | TASK-013, 014 |
| 03 | order-mq | `OrderMQAppServiceImpl` + `KafkaOrderConsumer` + `OutboxPublisherJob` | `FlashSale.Api.Workers.*` + `FlashSale.Application.Services.OrderMq*` | TASK-015, 016, 017 |
| 04 | payment | `PaymentController` + `PaymentAppServiceImpl` + `VnPayGatewayServiceImpl` | `FlashSale.Infrastructure.External` + `FlashSale.Application.Services.PaymentAppService` | TASK-018 |
| 05 | employee | `EmployeeController` + `EmployeeCacheService` | `FlashSale.Api.Controllers.EmployeeController` | TASK-019 |
| 06 | booking | `BookingController` + `BookingAppService` | `FlashSale.Api.Controllers.BookingController` | TASK-020 |
| 07 | demo | `HiController` + `SecureApiController` + `/ticket/ping/java` | `FlashSale.Api.Controllers.H*` + `S*` | TASK-020 |
| 08 | infrastructure | `redis` + `kafka` + `distributed.redisson` + `persistence.mapper` + `gateway` | `FlashSale.Infrastructure.*` | TASK-005..007 |