// Run: node --test web/test.mjs   (live tests need BETFAIR_APP_KEY / USERNAME / PASSWORD)
import test from 'node:test';
import http from 'node:http';
import assert from 'node:assert/strict';
import { up, down, snap, window, position, greenUp, LiquidityTracker, bookPct, short } from './public/ladder.js';
import { createServer } from './server.mjs';
import { matchScore, findLive } from './public/teams.js';
import { LiveFeed } from './live.mjs';

test('ladder steps', () => {
  for (const [f, t] of [[1.99, 2], [2, 2.02], [2.98, 3], [3, 3.05], [5.9, 6], [6, 6.2], [19.5, 20], [20, 21], [1000, 1000]]) assert.equal(up(f), t, `up ${f}`);
  for (const [f, t] of [[2, 1.99], [3, 2.98], [6, 5.9], [20, 19.5], [1.01, 1.01], [1000, 990]]) assert.equal(down(f), t, `down ${f}`);
  assert.equal(snap(2.019), 2.02);
  const w = window(3, 3, 3);
  assert.equal(w.length, 7); assert.equal(w[3], 3);
});

test('ladder window is full and distinct at the edges', () => {
  for (const c of [1.01, 1.02, 1000, 3]) {
    const w = window(c, 10, 10);
    assert.equal(w.length, 21); assert.equal(new Set(w).size, 21);
    assert.deepEqual(w, [...w].sort((a, b) => b - a));
  }
});

test('position and green up', () => {
  const o = (side, price, matched) => ({ selectionId: 7, side, price, matched, avgPrice: price });
  const pos = position([o('BACK', 4, 10)], 7);
  assert.deepEqual(pos, { ifWins: 30, ifLoses: -10 });
  assert.deepEqual(greenUp(pos, 3), { side: 'LAY', size: 13.33 });
  assert.deepEqual(greenUp(position([o('LAY', 2, 10)], 7), 2.5), { side: 'BACK', size: 8 });
  assert.equal(greenUp({ ifWins: 5, ifLoses: 5 }, 2), null);
});

test('liquidity tracker', () => {
  const run = (size) => ({ status: 'ACTIVE', ex: { availableToBack: [{ price: 2, size }], availableToLay: [{ price: 2.1, size: size * 2 }] } });
  const out = { status: 'REMOVED', ex: { availableToBack: [{ price: 9, size: 999 }], availableToLay: [{ price: 9, size: 999 }] } };
  const book = (matched, size) => ({ totalMatched: matched, runners: [run(size), run(size), out] });
  const t = new LiquidityTracker();
  t.add(book(1000, 50), 0);
  const s = t.add(book(1500, 50), 30000);
  assert.equal(s.perMin, 1000); assert.equal(s.back, 100); assert.equal(s.lay, 200);
  assert.equal(bookPct(book(0, 1), true), 100); assert.equal(bookPct(book(0, 1), false), 95.2);
  assert.equal(new LiquidityTracker().rate(0), 0);
  const small = new LiquidityTracker(3);
  for (let i = 0; i < 10; i++) small.add(book(1000 - i, 1), i * 1000);
  assert.equal(small.samples.length, 3); assert.equal(small.last.perMin, 0);
  assert.deepEqual([short(950), short(12500), short(1240000)], ['950', '12.5k', '1.2m']);
});

test('live-score team matching (same cases as the C# tests)', () => {
  const same = [['Buriram Utd v Beijing Guoan', 'Buriram United', 'Beijing Guoan'], ['Cong An Nhan Dan v Kashima', 'Công An Hà Nội', 'Kashima Antlers'],
    ['Viking v Bayern Munich', 'Viking FK', 'Bayern München'], ['Moreirense FC U23 v Santa Clara U23', 'Moreirense FC U23', 'Santa Clara U23'],
    ['Galatasaray U19 v Barcelona U19', 'Galatasaray U19', 'Barcelona U19'], ['Man Utd v Arsenal', 'Manchester United', 'Arsenal FC'],
    ['FC Giugliano v Inter Milan (Res)', 'Calcio Giugliano', 'Inter Milan U23']];
  for (const [e, h, a] of same) assert.ok(matchScore(e, h, a) > 0, e);
  const diff = [['Arsenal v Lille', 'Arsenal U19', 'Lille U19'], ['Arsenal v Lille', 'Arsenal Women', 'Lille Women'], ['Viking v Bayern Munich', 'Bayern Munich', 'Viking FK'],
    ['Arsenal v Chelsea', 'Liverpool', 'Everton'], ['Real Madrid v Barcelona', 'Real Sociedad', 'Barcelona'], ['Man Utd v Chelsea', 'Manchester City', 'Chelsea']];
  for (const [e, h, a] of diff) assert.equal(matchScore(e, h, a), 0, e);
  assert.equal(findLive('A v B', []), null);
});

test('live feed applies snapshots and goals', () => {
  const f = new LiveFeed();
  f.handle('{"type":"snapshot","matches":[{"sofa_event_id":0,"home_team":"Buriram United","away_team":"Beijing Guoan","home_score":0,"away_score":0}]}');
  assert.equal(findLive('Buriram Utd v Beijing Guoan', f.matches).homeScore, 0);
  f.handle('{"type":"goal","sofa_event_id":4866014,"home_team":"Buriram United","away_team":"Beijing Guoan","home_score":1,"away_score":0,"scoring_team":"home"}');
  assert.equal(findLive('Buriram Utd v Beijing Guoan', f.matches).homeScore, 1);
  assert.equal(f.matches.length, 1); assert.equal(f.alerts.length, 1); assert.ok(f.alerts[0].text.endsWith('(Buriram United)'));
  f.handle('not json'); f.handle('{}'); f.handle('{"type":"snapshot"}');
  assert.equal(f.matches.length, 1);
});

const live = process.env.BETFAIR_APP_KEY && process.env.BETFAIR_USERNAME && process.env.BETFAIR_PASSWORD;
test('server end-to-end against Betfair', { skip: !live }, async () => {
  const srv = createServer(); await new Promise(r => srv.listen(0, '127.0.0.1', r));
  const base = `http://127.0.0.1:${srv.address().port}`;
  const get = async p => (await fetch(base + p)).json();
  const post = async (p, b) => fetch(base + p, { method: 'POST', headers: { 'X-MT': '1' }, body: JSON.stringify(b) });
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
    assert.equal((await fetch(base + '/api/place', { method: 'POST', body: '{}' })).status, 403);   // no X-MT header: rejected
    const hostStatus = await new Promise((res, rej) => http.get({ host: '127.0.0.1', port: srv.address().port, path: '/api/status', headers: { Host: 'evil.example' } }, r => { r.resume(); res(r.statusCode); }).on('error', rej));
    assert.equal(hostStatus, 403);   // wrong Host header: rejected
    assert.equal((await fetch(base + '/..%2fserver.mjs')).status === 200, false);
  } finally { srv.close(); }
});

test('server starts without credentials and reports login state', async () => {
  const srv = createServer(''); await new Promise(r => srv.listen(0, '127.0.0.1', r));
  try {
    const st = await (await fetch(`http://127.0.0.1:${srv.address().port}/api/status`)).json();
    assert.equal(st.loggedIn, false); assert.equal(st.hasKey, false);
  } finally { srv.close(); }
});
