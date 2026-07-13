# Module 03 — Order MQ (Producer + Consumer + Outbox Publisher)

## Java → .NET

| Java | .NET |
|------|------|
| `com.xxxx.ddd.controller.http.OrderMQController` | `FlashSale.Api.Controllers.OrderMQController` |
| `com.xxxx.ddd.application.service.order.mq.OrderMQAppService` | `FlashSale.Application.Services.IOrderMqAppService` |
| `com.xxxx.ddd.application.service.order.mq.OrderMQAppServiceImpl` | `FlashSale.Application.Services.Implementations.OrderMqAppServiceImpl` |
| `com.xxxx.ddd.application.service.order.mq.KafkaOrderConsumer` | `FlashSale.Api.Workers.KafkaOrderConsumerWorker` (loop) + `FlashSale.Application.Services.IOrderMqConsumerHandler` (logic) |
| `com.xxxx.ddd.application.cronjob.OutboxPublisherJob` | `FlashSale.Api.Workers.OutboxPublisherWorker` |
| `com.xxxx.ddd.application.service.placeorder.mq.MQPlaceOrderServiceImpl` | `FlashSale.Application.Services.Implementations.MQPlaceOrderServiceImpl` |
| `com.xxxx.ddd.infrastructure.mq.KafkaOrderProducer` | `FlashSale.Infrastructure.Messaging.KafkaOrderProducer` |
| `com.xxxx.ddd.infrastructure.mq.PlaceOrderMQMessage` | `FlashSale.Contracts.Messages.PlaceOrderMqMessage` |

## Endpoints

| Method | Route | Behaviour |
|--------|-------|-----------|
| POST | `/order/mq` | Async place order, returns token |
| GET | `/order/mq/status/{token}` | Poll order_queue.status (0/1/2) |

## Kafka

| Topic | `order-place-topic` |
|-------|---------------------|
| Group ID | `order-consumer-group` |
| Concurrency | 10 partitions |
| Producer acks | `Acks.All` + `EnableIdempotence = true` |
| Message JSON | `{token, ticketId, quantity, userId, unitPrice, createdAt}` |

## Outbox flow

```
HTTP POST /order/mq
  ↓
Lua atomic decrement (PRO_TICKET:{id}:stock_available)
  ↓
EF transaction (single tx):
  - INSERT order_queue (token, ticketId, quantity, userId, status=0, ...)
  - INSERT outbox_event (aggregateId=token, eventType='ORDER_PLACED', payload=JSON)
  ↓
Return PlaceOrderResponse.Ok(token)
  ↓
OutboxPublisherWorker (1s tick):
  - SELECT ... FROM outbox_event WHERE status=0 ORDER BY created_at LIMIT 500 FOR UPDATE SKIP LOCKED
  - Kafka producer.SendAndAwaitAck
  - UPDATE outbox_event SET status=1, published_at=NOW()
  ↓
KafkaOrderConsumerWorker:
  - INSERT IGNORE idempotency_key (token, expires_at=now+24h)
  - if affected=0 → return (already processed)
  - DB: UPDATE ticket_item SET stock_available = stock_available - ? WHERE id = ? AND stock_available >= ?
  - INSERT ticket_order_{yyyyMM}
  - UPDATE order_queue SET status=1, order_number=...
```

## Tables

| Table | Purpose |
|-------|---------|
| `order_queue` | Async order tracking with token |
| `outbox_event` | Transactional outbox (status=0/1) |
| `idempotency_key` | Consumer dedupe (TTL 24h) |

## Tasks

- **TASK-015**: order_mq_producer — Lua + 1-tx outbox
- **TASK-016**: order_mq_consumer — Idempotency gate + DB decrement + insert
- **TASK-017**: order_mq_publisher — SKIP LOCKED cron

## Known quirks

- Java uses `ThreadLocalRandom.current().nextInt(1, 10)` for userId in MQ flow (demo)
- `MQ-{userId}-{tsMillis}` order number format in consumer path
- HTTP 200 with `success:false` body on error (Java)