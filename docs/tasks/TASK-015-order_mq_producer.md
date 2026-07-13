# TASK-015 — order_mq_producer

| Field | Value |
|-------|-------|
| Status | 🟡 pending |
| Branch | `f_task_015_order_mq_producer` |
| Module | order-mq |
| Phase | 1 — Feature port |
| Commit | — |
| Completed | — |

## Mục tiêu

Port `OrderMQAppServiceImpl.placeOrderMQ` — async place order flow. Redis Lua pre-deduct + INSERT order_queue + INSERT outbox_event trong **1 transaction**.

## Tệp Java nguồn (chỉ đọc)

- `xxxx-application/.../application/service/order/mq/OrderMQAppServiceImpl.java:38-104`
- `xxxx-application/.../application/service/order/mq/OrderMQAppService.java`
- `xxxx-application/.../application/service/placeorder/mq/MQPlaceOrderServiceImpl.java`
- `xxxx-application/.../application/service/placeorder/mq/MQPlaceOrderTokenServiceImpl.java`
- `xxxx-application/.../application/service/order/cache/StockOrderCacheService.java`
- `xxxx-controller/.../controller/http/OrderMQController.java`

## File .NET đích (sẽ tạo)

- `src/FlashSale.Application/Services/Implementations/OrderMqAppServiceImpl.cs`
- `src/FlashSale.Infrastructure/Persistence/Repositories/OrderQueueRepositoryImpl.cs`
- `src/FlashSale.Infrastructure/Persistence/Repositories/OutboxEventRepositoryImpl.cs`
- `src/FlashSale.Api/Controllers/OrderMQController.cs`

## Flow

```
1. Lua atomic decrement (PRO_TICKET:{id}:stock_available)
   if -1 → warm from DB, retry Lua
   if 0  → return failedQueue("OUT_OF_STOCK", "Hết vé")
2. unitPrice lookup
   if <=0 → compensate Redis, return failedQueue("PRICE_NOT_FOUND", ...)
3. Transactional write (use EF IDbContextTransaction OR TransactionScope):
   - INSERT order_queue (token, ticketId, quantity, userId, status=0, ...)
   - INSERT outbox_event (aggregateId=token, eventType='ORDER_PLACED', payload=JSON)
4. On transaction failure → compensate Redis (+quantity), return failedQueue("INTERNAL_ERROR", ...)
5. Return OrderQueue with status=0
```

## Outbox payload format

```json
{
  "token": "MQ-xxxxxxxxxxxxxxxx",
  "ticketId": 4,
  "quantity": 2,
  "userId": 5,
  "unitPrice": 10000,
  "createdAt": 1718246100123
}
```

## Endpoints

| Method | Route | Behaviour |
|--------|-------|-----------|
| POST | `/order/mq` | Place order async. Returns PlaceOrderResponse with token (when queued) or Code (when failed) |
| GET | `/order/mq/status/{token}` | Poll order_queue status (0=PENDING, 1=SUCCESS, 2=FAILED) |

## Acceptance criteria

- [ ] Transaction wraps both INSERTs in single DB tx
- [ ] Redis compensation on tx failure
- [ ] Token format = `MQ-` + 16-char UUID prefix (matches Java)
- [ ] Token unique constraint handled (retry on duplicate)
- [ ] Outbox payload JSON parseable by consumer (TASK-016 will verify)
- [ ] Unit + integration tests
- [ ] Update KNOWN_DIFFERENCES.md (random userId parity)

## Verification

```powershell
dotnet build FlashSale.slnx
dotnet test tests/FlashSale.UnitTests --filter "FullyQualifiedName~MqProducer"

# Smoke
curl -X POST http://localhost:5080/order/mq -H "Content-Type: application/json" \
  -d '{"ticketId":4,"quantity":2}'
# {"success":true,"orderNumber":"MQ-...","code":null,"message":null}

curl http://localhost:5080/order/mq/status/MQ-...
```

## Suggested commit

```
[TASK-015] order_mq_producer: lua pre-deduct + 1-tx order_queue + outbox_event
```