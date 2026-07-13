# TASK-023 — frontend_migrate

| Field | Value |
|-------|-------|
| Status | pending |
| Branch | `f_task_023_frontend_migrate` |
| Module | frontend |
| Phase | 3 — Frontend port (sau khi backend Phase 1 done) |
| Commit | — |
| Completed | — |

## Mục tiêu

Port `xxxx.fe.com` (React 19 + Vite 8) từ Java repo sang repo `flashsale/` mới,
đổi toàn bộ baseURL sang `.NET` API, wire Vite dev-proxy để tránh CORS,
bổ sung docker-compose service cho FE.

## Tệp nguồn (chỉ đọc)

`F:\TipJavascript\Microservice\xxxx.com-18-06-26\xxxx.fe.com\`:
- `package.json` — name `frontend`, scripts dev/build/lint/preview
- `vite.config.js` — minimal, chỉ có `react()` plugin
- `index.html`, `src/main.jsx`, `src/App.jsx` (router shell)
- `src/components/` (14 file):
  - `Home.jsx`, `Header.jsx`, `Footer.jsx`, `Hero.jsx`, `Features.jsx`,
    `WhyChooseUs.jsx`, `SearchBox.jsx`, `TicketListing.jsx`
  - `TicketsPage.jsx`, `TicketDetailPage.jsx`
  - `CartPage.jsx`, `BookingSuccessPage.jsx`
  - `ManagerPage.jsx` (admin CRUD + orders V1/V2 + tickets list)
- `src/services/api.js` — 2 axios instance: `ticketService` + `managerService`
- `src/assets/` — `hero.png`, `react.svg`, `vite.svg`

## File đích (sẽ tạo trong repo `flashsale/`)

```
flashsale/
├── frontend/
│   ├── .dockerignore
│   ├── .gitignore
│   ├── Dockerfile             # node:20-alpine → nginx:alpine multi-stage
│   ├── eslint.config.js
│   ├── index.html
│   ├── nginx.conf             # SPA fallback → /index.html
│   ├── package.json
│   ├── package-lock.json      # copy từ xxxx.fe.com
│   ├── README.md              # rewritten cho .NET backend
│   ├── vite.config.js         # add server.proxy → http://api:5080
│   ├── public/
│   └── src/
│       ├── App.css
│       ├── App.jsx
│       ├── index.css
│       ├── main.jsx
│       ├── assets/{hero.png,react.svg,vite.svg}
│       ├── components/  (14 .jsx files)
│       └── services/api.js    # baseURL: '/ticket', '/order', '/api'
```

## Changes so với Java

| Thay đổi | Java | .NET |
|---|---|---|
| Backend port | `1122` (application.yml) | `5080` (Kestrel) |
| Ticket baseURL | `http://localhost:1122/ticket` | `/ticket` (relative — via Vite proxy hoặc docker-compose internal DNS) |
| Order baseURL | `http://localhost:1122/order` | `/order` |
| Response envelope | `{ success, code, message, result }` (giữ nguyên) | giữ nguyên — backend .NET `ResultMessage<T>` mirror y hệt |
| Manager route | `/system/manager` | giữ nguyên |
| Locale | `vi-VN` | giữ nguyên |
| Design tokens | `src/index.css` | copy y nguyên |

### `vite.config.js` mới

```js
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/ticket':   { target: 'http://localhost:5080', changeOrigin: true },
      '/order':    { target: 'http://localhost:5080', changeOrigin: true },
      '/api':      { target: 'http://localhost:5080', changeOrigin: true },
      '/hello':    { target: 'http://localhost:5080', changeOrigin: true },
      '/metrics':  { target: 'http://localhost:5080', changeOrigin: true },
    },
  },
})
```

### `src/services/api.js` thay đổi

```diff
- const API_BASE_URL = 'http://localhost:1122/ticket';
+ const API_BASE_URL = '/ticket';   // Vite proxy → :5080
- const orderApi = axios.create({ baseURL: 'http://localhost:1122/order' });
+ const orderApi = axios.create({ baseURL: '/order' });

  ticketService.createBooking: async ({ ticketId, quantity }) => {
-   const response = await axios.post('http://localhost:1122/order/cas', ...)
+   const response = await axios.post('/order/cas', ...)
  }
```

### `docker-compose.yml` — bổ sung FE service

```yaml
  frontend:
    build: ./frontend
    container_name: flashsale-fe
    ports:
      - "5173:80"          # nginx inside container
    depends_on:
      - api
    networks:
      - flashsale-net
```

API service phải đổi `environment.ASPNETCORE_URLS` reference → internal DNS `http://api:5080`.

## Acceptance criteria

- [ ] Folder `flashsale/frontend/` chứa toàn bộ source gốc (không trộn với .NET code)
- [ ] `npm install && npm run dev` chạy được ở port 5173
- [ ] `npm run build` → `dist/` không lỗi, kích thước hợp lý (< 500KB gzip)
- [ ] Tất cả axios call dùng relative path (`/ticket`, `/order`, `/api`, `/hello`)
- [ ] Vite dev-proxy hoạt động (test với `npm run dev` + `dotnet run` đồng thời)
- [ ] Dockerfile build → image < 50MB
- [ ] docker-compose `frontend` service start, truy cập `http://localhost:5173` thấy UI
- [ ] Không có hard-coded URL backend trong source (trừ README)
- [ ] FE code giữ nguyên locale `vi-VN`, design tokens, tất cả 14 component

## Verification

```powershell
cd F:\TipJavascript\Microservice\flashsale\frontend

npm install
npm run dev &
# Mở http://localhost:5173 — kiểm tra 6 routes:
#   /                  → Home
#   /tickets           → TicketsPage
#   /ticket/:id        → TicketDetailPage
#   /cart              → CartPage
#   /booking-success   → BookingSuccessPage
#   /system/manager    → ManagerPage (4 tabs)

npm run build
npm run preview
npm run lint

# Docker
docker compose build frontend
docker compose up -d frontend
curl http://localhost:5173
docker compose logs frontend
```

## Suggested commit

```
[TASK-023] frontend_migrate: port xxxx.fe.com (React 19 + Vite 8) → flashsale/frontend, baseURL → relative + Vite proxy + docker
```
