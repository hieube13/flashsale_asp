# Module 08 — Infrastructure

Cross-cutting concerns. Foundation for every module.

## Java → .NET

| Java | .NET |
|------|------|
| `com.xxxx.ddd.infrastructure.cache.redis.RedisInfrasService` + `Impl` | `FlashSale.Infrastructure.Cache.IRedisInfrasService` + `RedisInfrasService` (StackExchange.Redis) |
| `com.xxxx.ddd.infrastructure.distributed.redisson.RedisDistributedLocker` + `Impl` + `Config` | `FlashSale.Infrastructure.DistributedLock.IDistributedLock` + `RedLockDistributedLockProvider` (RedLock.net) |
| `com.xxxx.ddd.infrastructure.mq.KafkaOrderProducer` | `FlashSale.Infrastructure.Messaging.KafkaOrderProducer` (Confluent.Kafka) |
| `com.xxxx.ddd.infrastructure.persistence.mapper.*` (8 JPA Mappers) | `FlashSale.Infrastructure.Persistence.Repositories.*` (EF Core impls) |
| `com.xxxx.ddd.infrastructure.config.RedisConfig` | DI registration in `Program.cs` |
| `com.xxxx.ddd.infrastructure.config.kafka.KafkaTopicConfig` | Kafka options in `appsettings.json` |
| `com.xxxx.ddd.infrastructure.gateway.VnPayGatewayServiceImpl` | `FlashSale.Infrastructure.External.VnPayGatewayService` |

## Configuration

| Java (application.yml) | .NET (appsettings.json) |
|------------------------|-------------------------|
| `server.port: 1122` | `Kestrel.ListenAnyIP(5080)` |
| `spring.datasource.url=jdbc:mysql://localhost:3316/vetautet` | `ConnectionStrings:MySql` |
| `spring.data.redis.host=127.0.0.1` `port=6319` | `Redis:ConnectionString=localhost:6319` |
| `spring.kafka.bootstrap-servers=localhost:9094` | `Kafka:BootstrapServers=localhost:9094` |
| `resilience4j.circuitbreaker.instances.checkRandom.*` | Polly v8 ResiliencePipeline |
| `management.endpoints.web.exposure.include: '*'` | ASP.NET HealthChecks + prometheus-net |

## Tasks

- **TASK-005**: infrastructure_data — EF Core DbContext, MySqlConnectionFactory
- **TASK-006**: infrastructure_redis — IRedisInfrasService + StackExchange.Redis concrete
- **TASK-007**: infrastructure_kafka — IKafkaOrderProducer + Confluent.Kafka concrete
- **TASK-018**: infrastructure_external — VnPayGatewayService

## Cross-cutting

- **Logging**: Serilog (Console sink + config reader). CorrelationId middleware in TASK-010.
- **Metrics**: prometheus-net. `/metrics` endpoint. Custom counters per module.
- **Health**: `/health` basic in scaffold, expanded in TASK-010 to ping MySQL/Redis/Kafka.

## Dapper for dynamic tables

Tables like `ticket_order_{yyyyMM}` cannot be modelled by EF Core (schema changes monthly).
Use `IDbConnectionFactory` + Dapper with table name as parameter:

```csharp
using var conn = await _factory.CreateOpenConnectionAsync(ct);
var sql = $"SELECT * FROM ticket_order_{yearMonth} WHERE id > @lastId ORDER BY id LIMIT @limit";
var rows = await conn.QueryAsync(sql, new { lastId, limit });
```

Validate yearMonth format before query to prevent SQL injection (`^\d{6}$`).