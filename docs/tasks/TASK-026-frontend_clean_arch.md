# TASK-026 — frontend_clean_arch

| Field | Value |
|-------|-------|
| Status | pending |
| Branch | `f_task_026_frontend_clean_arch` |
| Module | infra |
| Phase | Phase 4 — Clean Architecture alignment |
| Commit | — |
| Completed | — |

## Mục tiêu

Di chuyển `frontend/` từ `flashsale/frontend/` (flat, ngoài solution) vào `src/FlashSale.WebApp/`
để align với Clean Architecture structure của .NET solution. Frontend trở thành 1 project
ngang hàng với các layer khác (`Domain`, `Application`, `Infrastructure`, `Contracts`, `Api`).

## Điều kiện tiên quyết

- TASK-025 hoặc TASK-024 đã done (frontend đã ported thành công)

## Directory thay đổi

```
flashsale/
├── src/
│   ├── FlashSale.Domain/
│   ├── FlashSale.Application/
│   ├── FlashSale.Infrastructure/
│   ├── FlashSale.Contracts/
│   ├── FlashSale.Api/
│   └── FlashSale.WebApp/          ← frontend mới ở đây (hiện tại: flashsale/frontend/)
└── tests/
    └── ...
```

## Bước thực hiện

### 1. Tạo project placeholder

```bash
cd F:\TipJavascript\Microservice\flashsale

# Tạo thư mục
mkdir -p src/FlashSale.WebApp

# Copy toàn bộ nội dung frontend vào WebApp
Copy-Item -Recurse -Force frontend/* src/FlashSale.WebApp/
```

### 2. Cập nhật docker-compose.yml

Docker Compose hiện tại map `frontend/` trực tiếp. Cần cập nhật path:

```diff
  frontend:
    build:
-     context: ./frontend
+     context: ./src/FlashSale.WebApp
-     dockerfile: frontend/Dockerfile
+     dockerfile: Dockerfile
    container_name: flashsale.frontend
    ports:
      - "5173:80"
    depends_on:
      flashsale.api:
        condition: service_healthy
    networks:
      - flashsale-net
```

### 3. Cập nhật .dockerignore ở root

```diff
- frontend/
- frontend/node_modules
- frontend/dist
+ src/FlashSale.WebApp/
+ src/FlashSale.WebApp/node_modules
+ src/FlashSale.WebApp/dist
```

### 4. Cập nhật nginx environment files (nếu có)

Check `environment/nginx/docker-compose-*.yml` — tất cả `frontend` context paths cần update:

```diff
-   context: ../../frontend
+   context: ../../src/FlashSale.WebApp
```

### 5. Xóa thư mục frontend cũ

```bash
Remove-Item -Recurse -Force frontend/
```

### 6. Cập nhật README.md ở root (nếu có reference đến `frontend/`)

### 7. Kiểm tra `.gitignore` ở root

```diff
+ src/FlashSale.WebApp/
+ src/FlashSale.WebApp/node_modules
+ src/FlashSale.WebApp/dist
- frontend/
- frontend/node_modules
- frontend/dist
```

### 8. Docker build verification

```bash
docker compose build frontend
docker compose up -d frontend
curl http://localhost:5173
```

### 9. Update docs

- `INTERNAL_ARCHITECTURE.md` — cập nhật project structure để include `FlashSale.WebApp/`
- `FLASH_SALE_ARCHITECTURE.md` — cập nhật Module table
- `TASK_INDEX.md` — TASK-026 done, TASK-025 status
- `TIMELINE.md` — Phase 4 tasks

## Acceptance criteria

- [ ] `docker compose build frontend` → 0 error
- [ ] `docker compose up -d frontend` → container healthy
- [ ] `curl http://localhost:5173` → HTML 200 (React app)
- [ ] `/ticket/active` proxy qua Vite → .NET API → 200
- [ ] Thư mục `frontend/` đã xóa hoàn toàn
- [ ] Docs updated

## Verification

```powershell
# Build
docker compose build frontend

# Run
docker compose up -d frontend

# Check container
docker compose ps frontend

# Smoke test
curl http://localhost:5173
curl http://localhost:5173/ticket/active
```

## Rollback plan

```bash
git checkout -- frontend/     # restore deleted folder from git
git checkout -- docker-compose.yml
git checkout -- docs/INTERNAL_ARCHITECTURE.md
git checkout -- docs/FLASH_SALE_ARCHITECTURE.md
```

## Suggested commit

```
[TASK-026] frontend_clean_arch: move frontend/ → src/FlashSale.WebApp/ aligning with Clean Architecture project structure, update docker-compose context paths
```
