# TicketPro Frontend

React 19 + Vite 8 frontend for the TicketPro flash-sale platform. Connects to the `.NET 8` backend at `http://localhost:5080` via Vite dev proxy (no CORS).

## Quick Start

```bash
cd frontend
npm install
npm run dev        # http://localhost:5173
npm run build      # dist/
npm run preview    # preview build
npm run lint
```

## API Proxy (dev)

Vite proxies all `/ticket`, `/order`, `/api`, `/hello`, `/metrics`, `/payment`, `/health` to `http://localhost:5080`. No CORS needed in dev.

```js
// vite.config.js proxy config
'/ticket':  { target: 'http://localhost:5080', changeOrigin: true },
'/order':   { target: 'http://localhost:5080', changeOrigin: true },
'/api':     { target: 'http://localhost:5080', changeOrigin: true },
'/hello':   { target: 'http://localhost:5080', changeOrigin: true },
```

## Docker

```bash
# Build
docker compose build frontend

# Run
docker compose up -d frontend

# Logs
docker compose logs -f frontend

# Access
http://localhost:5173
```

Production uses Nginx alpine with SPA fallback routing.

## Routes

| Path | Component |
|------|-----------|
| `/` | Home (Hero + SearchBox + Features + TicketListing + WhyChooseUs) |
| `/tickets` | TicketsPage (filter + search + grid) |
| `/ticket/:id` | TicketDetailPage (booking sidebar) |
| `/cart` | CartPage (confirm + place order) |
| `/booking-success` | BookingSuccessPage |
| `/system/manager` | ManagerPage (4 tabs: create event, orders V1, orders V2, tickets list) |

## API Endpoints Used

| Service | Endpoint | Method |
|---------|----------|--------|
| ticketService | `/ticket/active` | GET |
| ticketService | `/ticket/{id}` | GET |
| ticketService | `/ticket/create` | POST |
| ticketService | `/ticket/{id}/active` | PUT |
| ticketService | `/ticket/{id}/inactive` | PUT |
| ticketService | `/ticket/{id}` | DELETE |
| ticketService | `/order/cas` | POST |
| managerService | `/order/1/list?ntable=` | GET |
| managerService | `/order/1/list/page?ntable=&cursor=&limit=` | GET |
| managerService | `/order/{userId}/{orderNumber}/cancel` | PUT |

## Tech Stack

- React 19
- Vite 8
- react-router-dom v7
- axios
- lucide-react (icons)
- ESLint 9 (flat config)
