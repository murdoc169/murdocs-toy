// Local web UI server. Binds to 127.0.0.1 only; credentials never leave this machine except to Betfair.
import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { Betfair, BetfairError } from './betfair.mjs';
import { LiveFeed } from './live.mjs';

/** Loads KEY=VALUE lines from .env.local files (web/ and the repo root) without overriding real environment variables. */
function loadEnvFiles() {
  const here = path.dirname(fileURLToPath(import.meta.url));
  for (const f of [path.join(here, '.env.local'), path.join(here, '..', '.env.local')]) {
    if (!existsSync(f)) continue;
    for (const line of readFileSync(f, 'utf8').split(/\r?\n/)) {
      const m = /^\s*([A-Z_][A-Z0-9_]*)\s*=\s*(.*)\s*$/.exec(line);
      if (m && process.env[m[1]] === undefined) process.env[m[1]] = m[2].replace(/^"(.*)"$/, '$1');
    }
  }
}
loadEnvFiles();

const favFile = path.join(path.dirname(fileURLToPath(import.meta.url)), 'favorites.json');
const readFavs = () => { try { return JSON.parse(readFileSync(favFile, 'utf8')); } catch { return []; } };
const favCompFile = path.join(path.dirname(fileURLToPath(import.meta.url)), 'favoritecompetitions.json');
const readFavComps = () => { try { return JSON.parse(readFileSync(favCompFile, 'utf8')); } catch { return []; } };

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), 'public');
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css' };

export function createServer(appKey = process.env.BETFAIR_APP_KEY) {
  const bf = new Betfair(appKey);
  const live = new LiveFeed();
  if (process.env.MT_RELAY_URL) live.start(process.env.MT_RELAY_URL);

  const routes = {
    'POST /api/login': async (b) => {
      if (b.appKey) bf.appKey = b.appKey;
      await bf.login(b.username || process.env.BETFAIR_USERNAME, b.password || process.env.BETFAIR_PASSWORD);
      return { ok: true };
    },
    'GET /api/status': async () => ({ loggedIn: bf.loggedIn, hasEnvCreds: !!(process.env.BETFAIR_USERNAME && process.env.BETFAIR_PASSWORD && bf.hasKey), hasKey: bf.hasKey }),
    'GET /api/volumes': (_, q) => bf.eventVolumes((q.get('ids') ?? '').split(',').filter(Boolean)),
    'GET /api/favorites': () => readFavs(),
    'POST /api/favorites': (b) => {
      if (!b.eventId || !b.name) throw new BetfairError('INVALID_INPUT');
      const list = readFavs().filter(f => f.eventId !== b.eventId);
      if (b.favorite) list.push({ eventId: String(b.eventId), name: String(b.name) });
      writeFileSync(favFile, JSON.stringify(list, null, 2));
      return { ok: true };
    },
    'GET /api/favoritecompetitions': () => readFavComps(),
    'POST /api/favoritecompetitions': (b) => {
      if (!b.competitionId || !b.name || !b.sportId) throw new BetfairError('INVALID_INPUT');
      const list = readFavComps().filter(f => f.competitionId !== String(b.competitionId));
      if (b.favorite) list.push({ competitionId: String(b.competitionId), name: String(b.name), sportId: String(b.sportId), region: b.region ?? null });
      writeFileSync(favCompFile, JSON.stringify(list, null, 2));
      return { ok: true };
    },
    'GET /api/live': () => live.snapshot(),
    'GET /api/eventtypes': () => bf.eventTypes(),
    'GET /api/markets': (_, q) => bf.markets({ eventTypeId: q.get('eventTypeId'), text: q.get('q') || undefined, inPlay: q.get('inPlay') === '1', marketType: q.get('marketType') || undefined }),
    'GET /api/competitions': (_, q) => bf.competitions({ eventTypeId: q.get('eventTypeId'), inPlay: q.get('inPlay') === '1' }),
    'GET /api/events': (_, q) => bf.events({ eventTypeId: q.get('eventTypeId'), competitionId: q.get('competitionId') || undefined, text: q.get('q') || undefined, inPlay: q.get('inPlay') === '1' }),
    'GET /api/eventmarkets': (_, q) => bf.eventMarkets(q.get('eventId')),
    'GET /api/market': async (_, q) => (await bf.markets({ marketIds: [q.get('marketId')] }))[0] ?? null,
    'GET /api/book': (_, q) => bf.book(q.get('marketId')),
    'GET /api/orders': (_, q) => bf.orders(q.get('marketId') || undefined),
    'GET /api/funds': () => bf.funds(),
    'POST /api/place': (b) => {
      const { marketId, selectionId, side, price, size, persistence } = b;
      if (!['BACK', 'LAY'].includes(side) || !(price >= 1.01 && price <= 1000) || !(size >= 1) || (persistence && !['LAPSE', 'PERSIST', 'MARKET_ON_CLOSE'].includes(persistence))) throw new BetfairError('INVALID_INPUT');
      return bf.place(marketId, { selectionId, side, price, size, persistence });
    },
    'POST /api/cancel': (b) => bf.cancel(b.marketId, b.betId),
  };

  const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, 'http://localhost');
    const send = (code, body, type = 'application/json') => { res.writeHead(code, { 'Content-Type': type, 'Cache-Control': 'no-store' }); res.end(typeof body === 'string' || Buffer.isBuffer(body) ? body : JSON.stringify(body)); };
    try {
      // Only this machine by name (blocks DNS rebinding), and POSTs need a header another site cannot send without a CORS preflight.
      if (!['localhost', '127.0.0.1'].includes((req.headers.host ?? '').split(':')[0])) return send(403, 'forbidden', 'text/plain');
      if (req.method === 'POST' && req.headers['x-mt'] !== '1') return send(403, { error: 'FORBIDDEN' });
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
      send(e instanceof BetfairError || e instanceof SyntaxError ? 400 : 500, { error: e.code ?? (e instanceof SyntaxError ? 'BAD_JSON' : 'ERROR'), message: e.message });
    }
  });
  server.on('close', () => live.stop());
  return server;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const port = Number(process.env.PORT ?? 8080);
  createServer().listen(port, '127.0.0.1', () => console.log(`MurdocsToy web UI: http://127.0.0.1:${port}`));
}
