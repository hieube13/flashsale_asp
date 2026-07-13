# TASK-016 — order_mq_consumer

| Field | Value |
|-------|-------|
| Status | 🟡 pending |
| Branch | `f_task_016_order_mq_consumer` |
| Module | order-mq |
| Phase | 1 — Feature port |
| Commit | — |
| Completed | — |

## Mục tiêu

Port `KafkaOrderConsumer` — Consume from `order-place-topic`, idempotency gate (INSERT IGNORE), DB decrement, insert order, update order_queue.

## Tệp Java nguồn (chỉ đọc)

- `xxxx-application/.../application/service/order/mq/KafkaOrderConsumer.java`
- `xxxx-domain/.../domain/respository/IdempotencyKeyRepository.java`

## File .NET đích (sẽ tạo)

- `src/FlashSale.Api/Workers/KafkaOrderConsumerWorker.cs` — concrete BackgroundService with Confluent.Kafka IConsumer
- `src/FlashSale.Application/Services/Implementations/OrderMqConsumerHandler.cs`
- `src/FlashSale.Infrastructure/Persistence/Repositories/IdempotencyKeyRepositoryImpl.cs`

## Consumer flow

```
1. Subscribe topic 'order-place-topic', group 'order-consumer-group'
2. Poll loop with concurrency=10 partitions (one worker per partition)
3. For each PlaceOrderMqMessage:
   - Build CancellationTokenSource with 30s timeout
   - handler.ProcessAsync(message, ct) (DI scope per message)
4. ProcessAsync:
   - try InsertIgnore idempotency_key (token, expires_at = now + 24h)
     - if affected=0 → already processed → return early
   - DB: TryDecreaseStockAsync(ticketId, quantity) — UPDATE ... WHERE stock_available >= ?
     - if 0 rows → compensate Redis (+quantity), update order_queue status=2 msg="Hết vé"
     - return
   - Insert TickerOrder into ticket_order_{yyyyMM} via Dapper
   - Update order_queue status=1, orderNumber=newly-generated
5. On exception → retry 3x with exponential backoff before giving up (commit offset anyway to avoid poison pill)
```

## Critical: idempotency key INSERT

MySQL: `INSERT IGNORE INTO idempotency_key(token, created_at, expires_at) VALUES (?, ?, ?)`.
Dapper: `connection.ExecuteAsync("INSERT IGNORE INTO ...", new {...})`.
Check affected rows: 1 = new, 0 = duplicate.

## Acceptance criteria

- [ ] Consumer subscribed, manual commit (auto.offset.reset=earliest)
- [ ] Idempotency gate BEFORE any side-effect (atomic via transaction)
- [ ] DB decrement uses optimistic WHERE clause (UPDATE ... WHERE stock_available >= ?)
- [ ] Redis compensate on OOS path
- [ ] Order number format `MQ-{userId}-{tsMillis}` (matches Java)
- [ ] At-least-once delivery + idempotency = exactly-once side-effect
- [ ] Unit test: same message processed twice → second is no-op
- [ ] Integration test: 100 messages with same token → 1 row in DB

## Verification

```powershell
dotnet build FlashSale.slnx
dotnet test tests/FlashSale.UnitTests --filter "FullyQualifiedName~MqConsumer"

# Run kafka topic in background, send test message
echo '{"token":"MQ-test","ticketId":4,"quantity":2,"userId":5,"unitPrice":10000,"createdAt":1718246100123}' | \
  docker exec -i flashsale.kafka kafka-console-producer.sh --bootstrap-server localhost:9092 --topic order-place-topic
```

## Suggested commit

```
[TASK-016] order_mq_consumer: confluent kafka loop + idempotency gate + db decrement + insert order
```