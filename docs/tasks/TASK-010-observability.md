# TASK-010 — observability

| Field | Value |
|-------|-------|
| Status | ✅ done (basic) |
| Branch | — |
| Module | api |
| Phase | 0 — Scaffold |
| Commit | — |
| Completed | 2026-07-13 |

## Mục tiêu

Serilog request logging + CorrelationId middleware + Prometheus metrics + /health expansion.

## Tệp Java nguồn (chỉ đọc)

- `xxxx-start/.../application.yml` — `management.endpoints.web.exposure.include: '*'`, `endpoint.prometheus.enabled: true`, metrics tags
- `xxxx-start/.../logback-spring.xml` — Java logging config (reference for naming)

## File .NET đích (đã tạo)

- `src/FlashSale.Api/Program.cs` — `UseSerilogRequestLogging()`, `UseHttpMetrics()`, `MapMetrics("/metrics")`
- `src/FlashSale.Api/appsettings.Development.json` — DetailedErrors=true

## What's done in scaffold

- ✅ Serilog request logging (`UseSerilogRequestLogging`)
- ✅ Prometheus `UseHttpMetrics` middleware (request count, duration, status)
- ✅ `/metrics` endpoint via `MapMetrics`
- ✅ Basic `/health` returning 200 OK

## What's added in TASK-010 proper (incremental)

- [ ] CorrelationId middleware (read `X-Correlation-Id` from header, push to `LogContext`)
- [ ] Expand `/health` to include MySQL/Redis/Kafka pings
- [ ] Custom metrics: `flashsale_orders_placed_total`, `flashsale_outbox_publish_duration_seconds`, `flashsale_redis_decrement_total{result=hit|miss|oos}`
- [ ] Serilog enricher: machine name, process id
- [ ] Serilog rolling file sink (optional)

## Verification

```powershell
curl http://localhost:5080/metrics
# HELP aspnetcore_http_requests_received_total ...
# TYPE aspnetcore_http_requests_received_total counter
```