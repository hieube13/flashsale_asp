# TASK-022 — cutover

| Field | Value |
|-------|-------|
| Status | 🟡 pending |
| Branch | `f_task_022_cutover` |
| Module | ops |
| Phase | 2 — Hardening |
| Commit | — |
| Completed | — |

## Mục tiêu

Cutover Java → .NET thông qua nginx routing: shadow traffic → 10% → 50% → 100%. Có rollback plan trong 5 phút.

## Prerequisites

- [ ] TASK-011..021 đã done, parity tests xanh
- [ ] Load tests pass trên .NET (k6 với 1000 RPS, p99 < 200ms)
- [ ] Kafka topic `order-place-topic` đã có consumer .NET chạy song song (idempotency gate đảm bảo không duplicate)
- [ ] MySQL/Redis cùng schema, .NET kết nối được
- [ ] Runbook rollback soạn xong

## Cutover phases

### Phase A — Shadow traffic (24h)

```nginx
location /order/cas {
    mirror /internal/dotnet-mirror;   # body copied to .NET but response from Java
    proxy_pass http://java:1122;
}

location /order/mq {
    mirror /internal/dotnet-mirror;
    proxy_pass http://java:1122;
}
# Other routes stay on Java
```

Compare .NET vs Java response for every mirrored request. Log diffs to ELK.

### Phase B — 10% traffic (24h)

```nginx
split_clients $request_id $dotNetUpstream {
    10%  dotnet;
    90%  java;
}

location /order/cas {
    proxy_pass $dotNetUpstream;
}
```

Watch error rate, latency p99, MySQL connection count, Redis ops/sec.

### Phase C — 50% traffic (48h)

Same split with 50/50.

### Phase D — 100% .NET

```nginx
location / {
    proxy_pass http://dotnet:5080;
}
```

Java app kept running but receives no traffic (still alive for rollback).

## Rollback (5 phút)

```bash
# Revert nginx config
kubectl apply -f nginx-rollback.yml

# Or in docker compose:
docker compose restart nginx
```

Rollback is config-only, no DB migration needed (DDL identical).

## Tệp Java nguồn (chỉ đọc)

- `xxxx-start/.../application.yml` — for final config verification
- `environment/nginx/` — Java nginx config for reference

## File .NET đích (sẽ tạo)

- `docs/CUTOVER.md` — full runbook
- `docs/ROLLBACK.md` — 5-minute rollback plan
- `environment/nginx/nginx.conf` — production routing
- `environment/nginx/nginx-shadow.conf` — Phase A
- `environment/nginx/nginx-10pct.conf` — Phase B
- `environment/nginx/nginx-50pct.conf` — Phase C
- `environment/nginx/nginx-100pct.conf` — Phase D
- `tests/FlashSale.LoadTests/k6/flash-sale.js` — adapted from Java k6 scripts

## Acceptance criteria

- [ ] Phase A: 24h shadow traffic, < 0.1% diff between Java and .NET response bodies
- [ ] Phase B: 10% traffic 24h, error rate < 0.1%, p99 latency comparable
- [ ] Phase C: 50% traffic 48h, MySQL pool not exhausted, Redis ops healthy
- [ ] Phase D: 100% traffic 24h, business metrics stable
- [ ] Rollback tested in staging before each phase advance
- [ ] Update TIMELINE.md with actual dates

## Verification

```powershell
# Phase D verification:
curl http://localhost:5080/health
curl http://localhost:5080/ticket/active

# Compare business metrics:
# - orders placed/min
# - payment success rate
# - kafka lag
# - redis hit ratio
```

## Suggested commit

```
[TASK-022] cutover: nginx shadow → 10/50/100 with rollback playbook
```