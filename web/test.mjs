// Run: node --test web/test.mjs   (live tests need BETFAIR_APP_KEY / USERNAME / PASSWORD)
import test from 'node:test';
import assert from 'node:assert/strict';
import { up, down, snap, window } from './public/ladder.js';
import { createServer } from './server.mjs';

test('ladder steps', () => {
  for (const [f, t] of [[1.99, 2], [2, 2.02], [2.98, 3], [3, 3.05], [5.9, 6], [6, 6.2], [19.5, 20], [20, 21], [1000, 1000]]) assert.equal(up(f), t, `up ${f}`);
  for (const [f, t] of [[2, 1.99], [3, 2.98], [6, 5.9], [20, 19.5], [1.01, 1.01], [1000, 990]]) assert.equal(down(f), t, `down ${f}`);
  assert.equal(snap(2.019), 2.02);
  const w = window(3, 3, 3);
  assert.equal(w.length, 7); assert.equal(w[3], 3);
});

const live = process.env.BETFAIR_APP_KEY && process.env.BETFAIR_USERNAME && process.env.BETFAIR_PASSWORD;
test('server end-to-end against Betfair', { skip: !live }, async () => {
  const srv = createServer(); await new Promise(r => srv.listen(0, '127.0.0.1', r));
  const base = `http://127.0.0.1:${srv.address().port}`;
  const get = async p => (await fetch(base + p)).json();
  const post = async (p, b) => fetch(base + p, { method: 'POST', body: JSON.stringify(b) });
  try {
    assert.equal((await post('/api/login', {})).status, 200);
    const types = await get('/api/eventtypes');
    const soccer = types.find(t => t.name === 'Soccer');
    const markets = await get(`/api/markets?eventTypeId=${soccer.id}&marketType=MATCH_ODDS`);
    assert.ok(markets.length > 0);
    const book = await get(`/api/book?marketId=${markets[0].marketId}`);
    assert.equal(book.marketId, markets[0].marketId);
    assert.ok(book.runners.length >= 2);
    assert.equal((await post('/api/place', { marketId: 'x', selectionId: 1, side: 'BACK', price: 0.5, size: 2 })).status, 400);
    assert.equal((await fetch(base + '/')).status, 200);
    assert.equal((await fetch(base + '/..%2fserver.mjs')).status === 200, false);
  } finally { srv.close(); }
});
