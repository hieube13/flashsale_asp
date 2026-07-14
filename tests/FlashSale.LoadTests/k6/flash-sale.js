/**
 * Flash Sale Load Test — CAS + MQ endpoints (.NET port)
 *
 * Usage:
 *   # Local .NET (no nginx):
 *   k6 run tests/FlashSale.LoadTests/k6/flash-sale.js \
 *       -e BASE_URL=http://localhost:5080
 *
 *   # Via nginx (all phases):
 *   k6 run tests/FlashSale.LoadTests/k6/flash-sale.js \
 *       -e BASE_URL=http://localhost
 *
 *   # CAS endpoint explicitly:
 *   k6 run tests/FlashSale.LoadTests/k6/flash-sale.js \
 *       -e ENDPOINT=/order/cas
 *
 *   # MQ endpoint:
 *   k6 run tests/FlashSale.LoadTests/k6/flash-sale.js \
 *       -e ENDPOINT=/order/mq
 *
 * Environment variables:
 *   BASE_URL      Target base URL  (default: http://localhost:5080)
 *   TICKET_ID     Ticket ID to order  (default: 1 — must exist)
 *   QUANTITY      Quantity per order  (default: 1)
 *   ENDPOINT      /order/cas or /order/mq  (default: /order/cas)
 *   STOCK         Initial stock for oversell guard  (default: 100)
 *   TOTAL_USERS   Total iterations (requests)  (default: 2000)
 *   VUS           Virtual users for CAS (default: 100) / MQ (default: 500)
 *
 * Thresholds:
 *   - p95 latency < 3s, p99 < 5s
 *   - HTTP error rate < 1%
 *   - Oversell guard: success orders <= STOCK
 */

import http from 'k6/http';
import { check } from 'k6';
import { Counter, Trend } from 'k6/metrics';

// ---------- Custom Metrics ----------
const ordersSuccess    = new Counter('orders_success');
const ordersOutOfStock = new Counter('orders_out_of_stock');
const ordersError      = new Counter('orders_error');
const orderLatency    = new Trend('order_latency_ms', true);

// ---------- Config ----------
const BASE_URL    = __ENV.BASE_URL    || 'http://localhost:5080';
const TICKET_ID   = parseInt(__ENV.TICKET_ID   || '1');
const QUANTITY    = parseInt(__ENV.QUANTITY    || '1');
const ENDPOINT    = __ENV.ENDPOINT    || '/order/cas';
const STOCK       = parseInt(__ENV.STOCK       || '100');
const TOTAL_USERS = parseInt(__ENV.TOTAL_USERS || '2000');
const VUS         = parseInt(__ENV.VUS         || '100');

// ---------- Scenarios ----------
export const options = {
  scenarios: {
    flash_rush: {
      executor: 'shared-iterations',
      vus:        VUS,
      iterations: TOTAL_USERS,
      maxDuration: '120s',
    },
  },
  thresholds: {
    'http_req_duration': ['p(95)<3000', 'p(99)<5000'],
    'http_req_failed':   ['rate<0.01'],
    'orders_success':    [`count<=${STOCK}`],
  },
  summaryTrendStats: ['min', 'med', 'avg', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

// ---------- Setup ----------
export function setup() {
  const res = http.get(`${BASE_URL}/ticket/${TICKET_ID}`);
  const ok  = check(res, { 'setup: ticket exists': (r) => r.status === 200 });
  if (!ok) {
    console.error(`[SETUP FAILED] ticketId=${TICKET_ID} unreachable at ${BASE_URL}, status=${res.status}`);
  } else {
    console.log(`[SETUP OK] base=${BASE_URL} ticketId=${TICKET_ID} stock=${STOCK} endpoint=${ENDPOINT} vus=${VUS} total=${TOTAL_USERS}`);
  }
}

// ---------- Main ----------
export default function () {
  const payload = JSON.stringify({ ticketId: TICKET_ID, quantity: QUANTITY });
  const params  = { headers: { 'Content-Type': 'application/json' } };

  const start   = Date.now();
  const res     = http.post(`${BASE_URL}${ENDPOINT}`, payload, params);
  const latency = Date.now() - start;
  orderLatency.add(latency);

  const httpOk = check(res, { 'HTTP 200': (r) => r.status === 200 });
  if (!httpOk) {
    ordersError.add(1);
    console.warn(`[ERROR] HTTP ${res.status}: ${res.body?.substring(0, 200)}`);
    return;
  }

  let result;
  try {
    result = res.json('result');
  } catch (_) {
    ordersError.add(1);
    console.warn(`[ERROR] Cannot parse JSON: ${res.body?.substring(0, 200)}`);
    return;
  }

  if (result?.success === true) {
    ordersSuccess.add(1);
  } else if (result?.code === 'OUT_OF_STOCK') {
    ordersOutOfStock.add(1);
  } else {
    ordersError.add(1);
    console.warn(`[UNEXPECTED] code=${result?.code} msg=${result?.message}`);
  }
}

// ---------- Summary ----------
export function handleSummary(data) {
  const m = data.metrics;

  const success = m.orders_success?.values?.count      || 0;
  const oos    = m.orders_out_of_stock?.values?.count || 0;
  const errors = m.orders_error?.values?.count        || 0;
  const total  = success + oos + errors;
  const rps    = (m.http_reqs?.values?.rate           || 0).toFixed(1);
  const p95    = (m.http_req_duration?.values?.['p(95)'] || 0).toFixed(0);
  const p99    = (m.http_req_duration?.values?.['p(99)'] || 0).toFixed(0);
  const oversell = success > STOCK
    ? `OVERSOLD  ${success} > ${STOCK}`
    : `OK        ${success} <= ${STOCK}`;

  const summary = `
╔══════════════════════════════════════════════════╗
║          FLASH SALE LOAD TEST — .NET PORT      ║
╠══════════════════════════════════════════════════╣
║  Target          : ${BASE_URL.padEnd(39)}║
║  Endpoint        : ${ENDPOINT.padEnd(39)}║
║  Ticket ID       : ${String(TICKET_ID).padEnd(39)}║
║  Stock (guard)   : ${String(STOCK).padEnd(39)}║
║  VUs             : ${String(VUS).padEnd(39)}║
║  Total requests  : ${String(TOTAL_USERS).padEnd(39)}║
╠══════════════════════════════════════════════════╣
║  Success orders  : ${String(success).padEnd(39)}║
║  Out-of-stock    : ${String(oos).padEnd(39)}║
║  Errors          : ${String(errors).padEnd(39)}║
║  Total           : ${String(total).padEnd(39)}║
╠══════════════════════════════════════════════════╣
║  Throughput (RPS): ${rps.padEnd(39)}║
║  Latency p95 (ms): ${p95.padEnd(39)}║
║  Latency p99 (ms): ${p99.padEnd(39)}║
╠══════════════════════════════════════════════════╣
║  Oversell guard  : ${oversell.padEnd(39)}║
╚══════════════════════════════════════════════════╝
`;

  return { stdout: summary };
}
