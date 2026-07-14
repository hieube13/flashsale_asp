# ROLLBACK.md — Emergency Rollback Plan

> **Time to execute**: &lt; 5 minutes
> **Scope**: Config-only change (nginx routing swap). No DB migration needed.
> **Trigger**: Error rate spike, p99 regression, data inconsistency, critical bug.

---

## When to Rollback

| Symptom | Threshold | Action |
|---------|-----------|--------|
| HTTP 5xx error rate | &gt; 1% for 5 min | Rollback |
| p99 latency | &gt; 10x Java baseline | Rollback |
| Payment success rate | &lt; 80% for 5 min | Rollback |
| MySQL pool exhausted | `Threads_connected &gt;= 90` for 2 min | Rollback |
| Kafka consumer lag | &gt; 50,000 for 10 min | Rollback |
| Data inconsistency | Orders missing / doubled | Immediate rollback |

---

## Rollback Steps

### Step 1 — Identify current phase

```bash
# Check which compose override is active
docker compose -f docker-compose.yml ps nginx
```

### Step 2 — Revert nginx to shadow (all traffic → Java)

```bash
docker compose -f docker-compose.yml \
  -f environment/nginx/docker-compose-shadow.yml up -d nginx
```

### Step 3 — Verify rollback succeeded (&lt; 2 min)

```bash
# Health should return Java response
curl http://localhost/health

# Verify no .NET traffic
docker logs flashsale.nginx 2>&1 | grep "dotnet_api" | wc -l
# Should be 0 (or very few in-flight)

# Verify Java is healthy
curl http://localhost/ticket/active
```

### Step 4 — Assess impact (&lt; 2 min)

```bash
# Orders placed during .NET window
# Check MySQL for any anomalies

mysql -h localhost -P 3316 -u root -proot1234 vetautet \
  -e "SELECT COUNT(*) as total_orders FROM ticket_order_$(date +%Y%m);"

# Check Redis stock consistency
redis-cli -p 6319 KEYS "PRO_TICKET:*:stock_available" | head -5
```

### Step 5 — Communicate

- Page on-call if not already engaged
- Notify: Slack/Teams channel
- Post-incident report within 24h

---

## Full Rollback to 100% Java (longer rollback)

If Java was never fully replaced or must be restored:

```bash
# Stop .NET container
docker compose -f docker-compose.yml stop flashsale.api

# Start Java container (assumes java-app:1122 is reachable)
docker compose -f docker-compose.yml up -d java-app

# Point nginx to Java only
docker compose -f docker-compose.yml \
  -f environment/nginx/docker-compose-shadow.yml up -d nginx

# Verify Java health
curl http://localhost/health
```

---

## Rollback Decision Tree

```
Error detected
    │
    ├── Is it a .NET-specific bug? (payments, employee, etc.)
    │       YES → Rollback to shadow (Phase A)
    │               Keep .NET running for debugging
    │
    ├── Is MySQL pool exhausted?
    │       YES → Check .NET connection pool config
    │               Increase PoolSize in appsettings, redeploy
    │               If still failing → Rollback
    │
    ├── Is Redis down?
    │       YES → .NET gracefully degrades (returns 503)
    │               Rollback if customer-facing endpoints affected
    │
    └── Is it a Kafka consumer lag spike?
            YES → Check .NET consumer throughput
            NO action needed if lag recovers within 30 min
            If lag &gt; 50k for 10 min → Rollback
```

---

## Post-Rollback Actions

1. **Investigate root cause**
   - Review `.NET` logs: `docker logs flashsale.api --tail=500`
   - Review `nginx` logs: `docker logs flashsale.nginx --tail=500`
   - Check Prometheus metrics for spike time

2. **Fix**
   - Deploy fix to .NET
   - Re-run load test (`k6`)
   - Re-advance to appropriate phase

3. **Document**
   - Write post-incident report
   - Update KNOWN_DIFFERENCES.md if new delta found
   - Update TIMELINE.md

---

## Contact Tree

| Level | Who | Contact |
|-------|-----|---------|
| P1 (full rollback) | Tech Lead + On-call | _______________ |
| P2 (shadow rollback) | On-call | _______________ |
| P3 (investigation) | Dev team | _______________ |

---

## Related

- Full runbook: `docs/CUTOVER.md`
- TASK-022: `docs/tasks/TASK-022-cutover.md`
- Prometheus: `http://localhost:9090`
- Grafana: `http://localhost:3000`
