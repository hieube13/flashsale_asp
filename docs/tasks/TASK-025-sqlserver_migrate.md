# TASK-025 — sqlserver_migrate

| Field | Value |
|-------|-------|
| Status | pending |
| Branch | `f_task_025_sqlserver_migrate` |
| Module | infra |
| Phase | Infra ops (post-migration) |
| Commit | — |
| Completed | — |

## Mục tiêu

Thay MySQL bằng SQL Server. Tất cả code DB-access (EF Core, Dapper) giữ nguyên
chỉ đổi provider + syntax; entities, services, controllers **không đổi**.

## Điều kiện tiên quyết

- TASK-024 done (tất cả 24 tasks hoàn thành)
- Docker Desktop running
- `dotnet --version` >= 8.0

## Thay đổi theo layer

### 1. NuGet packages

| Project | Remove | Add |
|---------|--------|-----|
| `FlashSale.slnx` | `Pomelo.EntityFrameworkCore.MySql` | `Microsoft.EntityFrameworkCore.SqlServer` |
| `FlashSale.slnx` | `MySqlConnector` | `Microsoft.Data.SqlClient` |

### 2. `src/FlashSale.Infrastructure/FlashSale.Infrastructure.csproj`

```diff
- <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="8.0.2" />
- <PackageReference Include="MySqlConnector" Version="2.4.0" />
+ <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.10" />
+ <PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.2" />
```

### 3. `src/FlashSale.Api/Program.cs`

```diff
- var mysqlConn = builder.Configuration.GetConnectionString("MySql")
-     ?? throw new InvalidOperationException("ConnectionStrings:MySql missing");
- opts.UseMySql(mysqlConn, ServerVersion.AutoDetect(mysqlConn)));
- builder.Services.Configure<MySqlOptions>(o => o.ConnectionString = mysqlConn);
- builder.Services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
+ var sqlConn = builder.Configuration.GetConnectionString("SqlServer")
+     ?? throw new InvalidOperationException("ConnectionStrings:SqlServer missing");
+ opts.UseSqlServer(sqlConn));
+ builder.Services.Configure<SqlServerOptions>(o => o.ConnectionString = sqlConn);
+ builder.Services.AddSingleton<IDbConnectionFactory, SqlServerConnectionFactory>();
```

### 4. `src/FlashSale.Infrastructure/Data/MySqlConnectionFactory.cs` → `SqlServerConnectionFactory.cs`

```csharp
// src/FlashSale.Infrastructure/Data/SqlServerConnectionFactory.cs
namespace FlashSale.Infrastructure.Data;

public sealed class SqlServerConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    public SqlServerConnectionFactory(IOptions<SqlServerOptions> options)
    {
        _connectionString = options.Value.ConnectionString
            ?? throw new InvalidOperationException("SqlServer:ConnectionString not configured");
    }
    public SqlConnection CreateConnection() => new(_connectionString);
}

public sealed class SqlServerOptions
{
    public string? ConnectionString { get; set; }
}
```

**Delete** `MySqlConnectionFactory.cs` + `MySqlOptions.cs`.

### 5. DDL — `environment/sqlserver/init/01-schema.sql` (NEW)

Chuyển toàn bộ `environment/mysql/init/01-schema.sql` sang SQL Server syntax:

| MySQL | SQL Server |
|-------|-----------|
| `` `backtick` `` | `[bracket]` |
| `AUTO_INCREMENT` | `IDENTITY(1,1)` |
| `TINYINT` | `TINYINT` (giữ nguyên) |
| `BIGINT` | `BIGINT` (giữ nguyên) |
| `DATETIME` | `DATETIME2(3)` |
| `VARCHAR(64)` | `NVARCHAR(64)` (Unicode support) |
| `LIMIT n` | `OFFSET 0 ROWS FETCH NEXT n ROWS ONLY` |
| `NOW()` | `GETUTCDATE()` |
| `DATE_FORMAT(dt, '%Y%m')` | `FORMAT(dt, 'yyyyMM', 'en-US')` |
| `UNIX_TIMESTAMP()` | `DATEDIFF(SECOND, '1970-01-01', dt)` |
| `ENGINE=InnoDB DEFAULT CHARSET=utf8mb4` | (xóa) |
| `ON DELETE CASCADE ON UPDATE CASCADE` | `ON DELETE CASCADE ON UPDATE CASCADE` (giữ nguyên) |
| `INSERT IGNORE` | `IF NOT EXISTS(SELECT 1 FROM table WHERE ...) INSERT...` |
| `DECIMAL(16,3)` | `DECIMAL(16,3)` (giữ nguyên) |
| Index inline | Index riêng `CREATE INDEX` |
| `fk_xxx` inline | `CONSTRAINT fk_xxx FOREIGN KEY...` riêng |

### 6. Repository raw SQL — cần duyệt lại

| File | MySQL syntax | Fix |
|------|-------------|-----|
| `IdempotencyKeyRepositoryImpl.cs` | `INSERT IGNORE` | → `IF NOT EXISTS(SELECT...) INSERT` |
| `OrderQueueRepositoryImpl.cs` | `ExecuteUpdateAsync` | → `ExecuteUpdateAsync` (EF Core 7+ — giữ nguyên) |
| `OutboxEventRepositoryImpl.cs` | `INSERT IGNORE` | → `IF NOT EXISTS...INSERT` |
| `OrderDeductionDomainService.cs` | `ExtractYearMonth` (C# regex) | → giữ nguyên (C#) |
| Shard table queries | Dynamic SQL `ticket_order_{yyyyMM}` | → giữ nguyên (Dapper parameterized) |

### 7. Docker — `docker-compose.yml`

```diff
- mysql:
-   image: mysql:8.0
-   container_name: flashsale.mysql
-   environment:
-     MYSQL_ROOT_PASSWORD: root1234
-     MYSQL_DATABASE: vetautet
-   ports:
-     - "3316:3306"
-   volumes:
-     - mysql-data:/var/lib/mysql
-     - ./environment/mysql/init:/docker-entrypoint-initdb.d:ro
-   healthcheck:
-     test: ["CMD", "mysqladmin", "ping", "-h", "localhost", "-uroot", "-proot1234"]

+ mssql:
+   image: mcr.microsoft.com/mssql/server:2022-latest
+   container_name: flashsale.mssql
+   environment:
+     ACCEPT_EULA: Y
+     MSSQL_SA_PASSWORD: Test@Pass1234
+     MSSQL_DATABASE: vetautet
+   ports:
+     - "1433:1433"
+   volumes:
+     - mssql-data:/var/opt/mssql
+     - ./environment/sqlserver/init:/init:ro
+   healthcheck:
+     test: ["CMD-SHELL", "sqlcmd -S localhost -U sa -P Test@Pass1234 -Q 'SELECT 1'"]

  flashsale.api:
-   ConnectionStrings__MySql: "server=mysql;port=3306;database=vetautet;user=root;password=root1234;Pooling=true;MaximumPoolSize=100;MinimumPoolSize=20;"
+   ConnectionStrings__SqlServer: "Server=mssql,1433;Database=vetautet;User Id=sa;Password=Test@Pass1234;TrustServerCertificate=True;Pooling=true;Max Pool Size=100;Min Pool Size=20;"

- depends_on mysql condition: service_healthy
+ depends_on mssql condition: service_healthy

- volumes:
-   mysql-data:
+ volumes:
+   mssql-data:
```

### 8. `appsettings.json`

```diff
- "ConnectionStrings": {
-   "MySql": "Server=localhost;Port=3316;Database=vetautet;User=root;Password=root1234;Pooling=true;..."
+ "ConnectionStrings": {
+   "SqlServer": "Server=localhost,1433;Database=vetautet;User Id=sa;Password=Test@Pass1234;TrustServerCertificate=True;Pooling=true;..."
```

### 9. `Dockerfile`

```diff
- # Multi-stage build giữ nguyên, chỉ cập nhật connection string trong docker-compose
```

### 10. ArchitectureTests

Kiểm tra `NetArchTest` vẫn pass. Không cần thay đổi vì architecture graph giữ nguyên.

## Acceptance criteria

- [ ] `dotnet build FlashSale.slnx` → 0 error
- [ ] `dotnet test tests/FlashSale.UnitTests` → green
- [ ] `dotnet test tests/FlashSale.ArchitectureTests` → green
- [ ] Docker containers start: `mssql` healthy, `redis` healthy
- [ ] `.NET API` kết nối SQL Server, `/health` → 200
- [ ] `/ticket/active` → 200 với data từ SQL Server
- [ ] Smoke test 5 endpoints cơ bản đều 200 OK

## Verification

```powershell
cd F:\TipJavascript\Microservice\flashsale
docker compose up -d mssql redis

# Chờ mssql ready
Start-Sleep 15

# Apply DDL
docker exec -i flashsale.mssql /opt/mssql-tools/bin/sqlcmd \
  -S localhost -U sa -P Test@Pass1234 -d vetautet \
  < environment/sqlserver/init/01-schema.sql

# Run API
dotnet run --project src/FlashSale.Api

# Smoke
curl http://localhost:5080/health
curl http://localhost:5080/ticket/active
```

## Rollback plan

1. `git checkout` revert NuGet + code changes
2. `docker compose down mssql`
3. `docker compose up -d mysql`
4. Rebuild API

## Suggested commit

```
[TASK-025] sqlserver_migrate: Pomelo MySQL → EF Core SqlServer + Microsoft.Data.SqlClient, DDL rewritten to T-SQL, docker-compose mssql container
```
