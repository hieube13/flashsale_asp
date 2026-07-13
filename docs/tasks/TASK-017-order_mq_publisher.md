# TASK-017 — order_mq_publisher

| Field | Value |
|-------|-------|
| Status | 🟡 pending |
| Branch | `f_task_017_order_mq_publisher` |
| Module | order-mq |
| Phase | 1 — Feature port |
| Commit | — |
| Completed | — |

## Mục tiêu

Port `OutboxPublisherJob` — Cron 1s, SELECT FOR UPDATE SKIP LOCKED on `outbox_event WHERE status=0`, gửi Kafka, mark PUBLISHED. Multi-instance safe.

## Tệp Java nguồn (chỉ đọc)

- `xxxx-application/.../application/cronjob/OutboxPublisherJob.java`

## File .NET đích (sẽ tạo)

- `src/FlashSale.Api/Workers/OutboxPublisherWorker.cs` — concrete BackgroundService, `PeriodicTimer(1s)`
- `src/FlashSale.Application/Services/Implementations/OutboxPublisherService.cs` — the actual loop logic
- `src/FlashSale.Infrastructure/Persistence/Repositories/OutboxEventRepositoryImpl.cs` — `FindPendingForUpdateSkipLockedAsync(int batchSize)`

## Flow (row-by-row, Java default)

```
1. PeriodicTimer tick every 1s
2. open transaction
   SELECT * FROM outbox_event WHERE status = 0 ORDER BY created_at LIMIT 500 FOR UPDATE SKIP LOCKED
   // ^ multi-instance safe: each replica takes different rows
3. for each event:
   - deserialize JSON payload → PlaceOrderMqMessage
   - producer.SendAndAwaitAckAsync(topic, key=event.aggregateId, message)
   - on ACK: UPDATE outbox_event SET status=1, published_at=NOW() WHERE id=?
   - on fail: log error, leave row status=0 for next tick
4. close transaction (commits all UPDATEs together)
```

## Acceptance criteria

- [ ] Cron fixedDelay=1s (PeriodicTimer)
- [ ] SELECT FOR UPDATE SKIP LOCKED (multi-instance safe)
- [ ] Send awaits broker ACK (Acks.All + EnableIdempotence)
- [ ] Failure does NOT block subsequent events
- [ ] Stop the worker mid-cycle → next start picks up where left off (status=0 rows still pending)
- [ ] Integration test: spin 2 replicas, 100 events → both produce, no double-publish
- [ ] Metric: `flashsale_outbox_publish_total{result=success|fail}`

## Verification

```powershell
dotnet build FlashSale.slnx
dotnet test tests/FlashSale.UnitTests --filter "FullyQualifiedName~OutboxPublisher"

# Smoke
# 1. Insert outbox_event manually
mysql -h 127.0.0.1 -P 3316 -uroot -proot1234 vetautet -e \
  "INSERT INTO outbox_event (aggregate_id, event_type, payload, status, created_at) VALUES ('MQ-test', 'ORDER_PLACED', '{...}', 0, NOW());"
# 2. Watch logs — should see "published eventId=..."
docker compose logs -f flashsale.api | grep OutboxPublisher
```

## Suggested commit

```
[TASK-017] order_mq_publisher: skip-locked cron with broker ack and status update
```