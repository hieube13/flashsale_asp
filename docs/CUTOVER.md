# CUTOVER.md — Java → .NET Migration Runbook

> **Owner**: DevOps / Tech Lead
> **Duration**: Phase A (24h shadow) → Phase B (24h 10%) → Phase C (48h 50%) → Phase D (24h 100%)
> **Rollback**: &lt; 5 minutes — config-only, no DB migration

---

## Prerequisites (must all pass before Phase A)

| Check | Command | Pass criteria |
|-------|---------|--------------|
| .NET build | `dotnet build` | 0 errors |
| Unit tests | `dotnet test tests/FlashSale.UnitTests` | 100% green |
| Arch tests | `dotnet test tests/FlashSale.ArchitectureTests` | 5/5 green |
| Contract tests (infra) | `docker compose up -d && dotnet run --project src/FlashSale.Api && dotnet test tests/FlashSale.ContractTests` | 40/40 green |
| .NET health | `curl http://localhost:5080/health` | `{"status":"ok"}` |
| Java health | `curl http://java-app:1122/health` | HTTP 200 |
| MySQL reachable | `mysql -h localhost -P 3316 -u root -proot1234 -e "SELECT 1"` | 1 row |
| Redis reachable | `redis-cli -p 6319 PING` | PONG |
| Kafka reachable | `kafka-topics.sh --bootstrap-server localhost:9094 --list` | lists topics |
| k6 installed | `k6 version` | &gt;= 0.45 |

---

## Cutover Phases

### Phase A — Shadow Traffic (24 hours)

**Goal**: Diff .NET vs Java response bodies without affecting users.

```bash
# Start with shadow routing (all traffic → Java, mirrored to .NET)
docker compose -f docker-compose.yml \
  -f environment/nginx/docker-compose-shadow.yml up -d

# Verify nginx is routing
curl http://localhost/health          # should return .NET response
curl http://localhost/api/bookings   # should return Java response
```

**Shadow targets**: `/order/cas`, `/order/mq`

**What to watch** (Prometheus → Grafana):
- `nginx_http_requests_total{backend="dotnet"}` vs `{backend="java"}`
- Error rate diff between backends
- Response body diffs in nginx logs

**Advance criteria**: shadow diff rate &lt; 0.1% over 24 hours

```bash
# Check shadow diffs
docker logs flashsale.nginx 2>&1 | grep -i "mismatch\|diff\|error" | head -20
```

---

### Phase B — 10% Traffic (24 hours)

**Goal**: 10% of real traffic goes to .NET, 90% stays on Java.

```bash
# Switch to 10% routing
docker compose -f docker-compose.yml \
  -f environment/nginx/docker-compose-10pct.yml up -d
```

**What to watch**:
```bash
# Error rate
curl -s http://localhost:9090/api/v1/query?query=rate(http_requests_total{code=~"5.."}[5m])

# p99 latency
curl -s http://localhost:9090/api/v1/query?query=histogram_quantile(0.99, rate(http_request_duration_seconds_bucket[5m]))

# MySQL pool
curl -s http://localhost:9090/api/v1/query?query=mysql_global_status_threads_connected

# Redis ops/sec
curl -s http://localhost:9090/api/v1/query?query=rate(redis_commands_processed_total[5m])
```

**Advance criteria**:
- Error rate &lt; 0.1%
- p99 latency &lt; 200ms (comparable to Java baseline)
- MySQL max_pool not exhausted
- Redis ops healthy

---

### Phase C — 50% Traffic (48 hours)

**Goal**: Equal split, stress-test .NET under sustained load.

```bash
docker compose -f docker-compose.yml \
  -f environment/nginx/docker-compose-50pct.yml up -d
```

**What to watch** (all Phase B metrics, plus):
```bash
# Kafka consumer lag
curl -s http://localhost:9090/api/v1/query?query=kafka_consumer_lag_sum

# .NET vs Java orders/min (should be ~equal)
curl -s http://localhost:9090/api/v1/query?query=rate(orders_placed_total{backend="dotnet"}[5m])
curl -s http://localhost:9090/api/v1/query?query=rate(orders_placed_total{backend="java"}[5m])
```

**Advance criteria**:
- 48h error rate &lt; 0.1%
- MySQL max_pool not exhausted
- Kafka consumer lag &lt; 1000 messages
- p99 no regression vs Phase B

---

### Phase D — 100% .NET (24 hours, then lock in)

**Goal**: All traffic on .NET, Java kept alive for rollback.

```bash
docker compose -f docker-compose.yml \
  -f environment/nginx/docker-compose-100pct.yml up -d
```

**Final verification**:
```bash
# All traffic should hit .NET
curl http://localhost/health
curl http://localhost/ticket/active
curl -X POST http://localhost/order/cas \
  -H "Content-Type: application/json" \
  -d '{"ticketId":1,"quantity":1}'

# Verify Java receives zero traffic
docker logs flashsale.nginx 2>&1 | grep -c "java_api"   # should be 0
```

**Acceptance**:
- 24h error rate &lt; 0.1%
- Business metrics stable (orders/min, payment success rate)
- Java app can be stopped (no traffic dependency)

---

## Business Metrics Dashboard

Grafana dashboard: `http://localhost:3000` (admin / admin)

| Metric | Threshold | Query |
|--------|-----------|-------|
| Orders placed / min | &gt; 0 | `rate(orders_placed_total[1m]) * 60` |
| Payment success rate | &gt; 95% | `rate(payment_success_total[5m]) / rate(payment_total[5m]) * 100` |
| Kafka consumer lag | &lt; 1000 | `kafka_consumer_lag_sum` |
| Redis hit ratio | &gt; 80% | `redis_keyspace_hits / (redis_keyspace_hits + redis_keyspace_misses)` |
| MySQL active connections | &lt; 80 | `mysql_global_status_threads_connected` |
| API p99 latency | &lt; 200ms | `histogram_quantile(0.99, rate(http_request_duration_seconds_bucket[5m]))` |

---

## Load Test (Before Each Phase)

```bash
# CAS endpoint
k6 run tests/FlashSale.LoadTests/k6/flash-sale.js \
  -e BASE_URL=http://localhost \
  -e ENDPOINT=/order/cas \
  -e STOCK=100 \
  -e VUS=100 \
  -e TOTAL_USERS=2000

# MQ endpoint
k6 run tests/FlashSale.LoadTests/k6/flash-sale.js \
  -e BASE_URL=http://localhost \
  -e ENDPOINT=/order/mq \
  -e STOCK=1000 \
  -e VUS=500 \
  -e TOTAL_USERS=5000
```

Pass criteria: p99 &lt; 5s, error rate &lt; 1%, no oversell.

---

## Roles & Contacts

| Role | Responsibility |
|------|---------------|
| Tech Lead | Approve phase advance / rollback |
| DevOps | Execute docker compose commands, monitor dashboards |
| On-call | Respond to alerts during cutover window |

---

## Emergency Contacts

- Primary on-call: _______________
- Secondary: _______________
- Escalation: _______________

---

## References

- Rollback: `docs/ROLLBACK.md`
- TASK-022: `docs/tasks/TASK-022-cutover.md`
- TIMELINE: `docs/TIMELINE.md`
- Java k6 baseline: `F:\TipJavascript\Microservice\xxxx.com-18-06-26\benchmark\k6\flash-sale.js`
