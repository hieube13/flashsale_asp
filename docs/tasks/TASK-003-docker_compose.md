# TASK-003 — docker_compose

| Field | Value |
|-------|-------|
| Status | ✅ done |
| Branch | — |
| Module | infra |
| Phase | 0 — Scaffold |
| Commit | — |
| Completed | 2026-07-13 |

## Mục tiêu

docker-compose chạy MySQL + Redis + Kafka + Prometheus + Grafana trên cùng network. Port mapping giống Java (MySQL 3316, Redis 6319, Kafka 9094).

## Tệp Java nguồn (chỉ đọc)

- `xxxx.com-18-06-26/environment/docker-compose-dev.yml` — Java reference stack
- `xxxx.com-18-06-26/environment/docker-compose-kafka.yml` — Kafka KRaft single-node
- `xxxx.com-18-06-26/environment/mysql/init/ticket_init.sql` — schema (mounted)

## File .NET đích (đã tạo)

- `docker-compose.yml`
- `Dockerfile`
- `.dockerignore`
- `.env.example`
- `appsettings.json` (mirror ports/configs)

## Checklist

- [x] mysql 8.0 on port 3316 (db `vetautet`)
- [x] redis 7-alpine on port 6319
- [x] kafka 3.7 KRaft single-node on port 9094 (controller 9093)
- [x] prometheus on port 9090
- [x] grafana on port 3000
- [x] flashsale.api on port 5080 with all deps
- [x] Volume mount `environment/mysql/init/ticket_init.sql` into MySQL init
- [x] Healthchecks cho mysql/redis
- [x] Network `flashsale-net` cho inter-service DNS

## Verification

```powershell
docker compose up -d mysql redis kafka
docker compose ps
# MySQL healthy, Redis healthy, Kafka started
```