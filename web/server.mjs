// Local web UI server. Binds to 127.0.0.1 only; credentials never leave this machine except to Betfair.
import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { Betfair, BetfairError } from './betfair.mjs';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), 'public');
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css' };

export function createServer(appKey = process.env.BETFAIR_APP_KEY) {
  const bf = new Betfair(appKey);

  const routes = {
    'POST /api/login': async (b) => {
      await bf.login(b.username || process.env.BETFAIR_USERNAME, b.password || process.env.BETFAIR_PASSWORD);
      return { ok: true };
    },
    'GET /api/status': async () => ({ loggedIn: bf.loggedIn, hasEnvCreds: !!(process.env.BETFAIR_USERNAME && process.env.BETFAIR_PASSWORD) }),
    'GET /api/eventtypes': () => bf.eventTypes(),
    'GET /api/markets': (_, q) => bf.markets({ eventTypeId: q.get('eventTypeId'), text: q.get('q') || undefined, inPlay: q.get('inPlay') === '1', marketType: q.get('marketType') || undefined }),
    'GET /api/book': (_, q) => bf.book(q.get('marketId')),
    'GET /api/orders': () => bf.orders(),
    'GET /api/funds': () => bf.funds(),
    'POST /api/place': (b) => {
      const { marketId, selectionId, side, price, size } = b;
      if (!['BACK', 'LAY'].includes(side) || !(price >= 1.01 && price <= 1000) || !(size >= 1)) throw new BetfairError('INVALID_INPUT');
      return bf.place(marketId, { selectionId, side, price, size });
    },
    'POST /api/cancel': (b) => bf.cancel(b.marketId, b.betId),
  };

  return http.createServer(async (req, res) => {
    const url = new URL(req.url, 'http://localhost');
    const send = (code, body, type = 'application/json') => { res.writeHead(code, { 'Content-Type': type, 'Cache-Control': 'no-store' }); res.end(typeof body === 'string' || Buffer.isBuffer(body) ? body : JSON.stringify(body)); };
    try {
      const handler = routes[`${req.method} ${url.pathname}`];
      if (handler) {
        let body = {};
        if (req.method === 'POST') { let s = ''; for await (const c of req) s += c; body = s ? JSON.parse(s) : {}; }
        return send(200, (await handler(body, url.searchParams)) ?? {});
      }
      if (req.method === 'GET') {
        const file = url.pathname === '/' ? '/index.html' : url.pathname;
        const full = path.normalize(path.join(root, file));
        if (!full.startsWith(root)) return send(403, 'forbidden', 'text/plain');
        return send(200, await readFile(full), types[path.extname(full)] ?? 'application/octet-stream');
      }
      send(404, { error: 'not found' });
    } catch (e) {
      if (e.code === 'ENOENT') return send(404, 'not found', 'text/plain');
      send(e instanceof BetfairError ? 400 : 500, { error: e.code ?? 'ERROR', message: e.message });
    }
  });
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const port = Number(process.env.PORT ?? 8080);
  if (!process.env.BETFAIR_APP_KEY) { console.error('Set BETFAIR_APP_KEY (and optionally BETFAIR_USERNAME / BETFAIR_PASSWORD).'); process.exit(1); }
  createServer().listen(port, '127.0.0.1', () => console.log(`MurdocsToy web UI: http://127.0.0.1:${port}`));
}
