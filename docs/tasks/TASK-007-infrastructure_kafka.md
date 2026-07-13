# TASK-007 — infrastructure_kafka

| Field | Value |
|-------|-------|
| Status | ✅ done (stub) |
| Branch | — |
| Module | infra |
| Phase | 0 — Scaffold |
| Commit | — |
| Completed | 2026-07-13 |

## Mục tiêu

Kafka producer (Confluent.Kafka) + consumer worker stub. Topic name `order-place-topic` match Java. Consumer group `order-consumer-group` match Java.

## Tệp Java nguồn (chỉ đọc)

- `xxxx-infrastructure/.../infrastructure/mq/KafkaOrderProducer.java`
- `xxxx-infrastructure/.../infrastructure/mq/PlaceOrderMQMessage.java`
- `xxxx-application/.../application/service/order/mq/KafkaOrderConsumer.java` (reference for actual consumer logic)
- `xxxx-start/.../application.yml` — kafka.bootstrap-servers: localhost:9094

## File .NET đích (đã tạo / sẽ tạo)

- `src/FlashSale.Infrastructure/Messaging/IKafkaOrderProducer.cs`
- `src/FlashSale.Infrastructure/Messaging/KafkaOrderProducer.cs` — Confluent.Kafka, `Acks=All`, idempotent producer
- `src/FlashSale.Infrastructure/Messaging/KafkaOptions.cs`
- `src/FlashSale.Api/Workers/KafkaOrderConsumerWorker.cs` — BackgroundService stub, concrete impl in TASK-016

## Checklist

- [x] Producer with `Acks.All` + `EnableIdempotence = true`
- [x] Topic name matches Java: `order-place-topic`
- [x] Group ID matches Java: `order-consumer-group`
- [x] `SendAndAwaitAckAsync` blocks until broker ACK (mirrors Java's `sendAndAwaitAck`)
- [ ] **Concrete consumer loop** — added in TASK-016 (with concurrency=10 partitions)
- [ ] **Deserializer** — PlaceOrderMqMessage JSON via System.Text.Json

## Verification

```powershell
dotnet build src/FlashSale.Infrastructure/FlashSale.Infrastructure.csproj
dotnet build src/FlashSale.Api/FlashSale.Api.csproj
```