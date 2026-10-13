// Betfair price increments. Shared by the UI and tests.
const BANDS = [[2, 0.01], [3, 0.02], [4, 0.05], [6, 0.1], [10, 0.2], [20, 0.5], [30, 1], [50, 2], [100, 5], [1000, 10]];
export const MIN = 1.01, MAX = 1000;
const r2 = v => Math.round(v * 100) / 100;
const step = p => (BANDS.find(([up]) => p < up - 1e-9) ?? [0, 10])[1];

export function snap(p) {
  p = Math.min(MAX, Math.max(MIN, p));
  const s = step(p);
  return r2(Math.round(p / s) * s);
}
export const up = p => { p = snap(p); return p >= MAX ? MAX : snap(p + step(p)); };
export const down = p => { p = snap(p); return p <= MIN ? MIN : snap(p - step(p - 1e-4)); };

/** Prices descending, about `above` ticks over the centre and `below` under it; slides near the ends so it always has above+below+1 distinct prices. */
export function window(centre, above, below) {
  const total = above + below + 1;
  let p = snap(centre);
  for (let i = 0; i < above && p < MAX; i++) p = up(p);
  const out = [];
  for (let i = 0; i < total; i++) { out.push(p); if (p <= MIN) break; p = down(p); }
  p = out[0];
  while (out.length < total && p < MAX) { p = up(p); out.unshift(p); }
  return out;
}

/** Profit if the runner wins / loses, from matched bets (same maths as the desktop app). */
export function position(orders, selectionId) {
  let win = 0, lose = 0;
  for (const o of orders) {
    if (o.selectionId !== selectionId || !(o.matched > 0)) continue;
    const price = o.avgPrice > 0 ? o.avgPrice : o.price;
    if (o.side === 'BACK') { win += o.matched * (price - 1); lose -= o.matched; }
    else { win -= o.matched * (price - 1); lose += o.matched; }
  }
  return { ifWins: r2(win), ifLoses: r2(lose) };
}
/** The bet at `price` that leaves equal profit either way, or null when already level. */
export function greenUp(pos, price) {
  const h = (pos.ifLoses - pos.ifWins) / price;
  const size = r2(Math.abs(h));
  return size < 0.01 ? null : { side: h > 0 ? 'BACK' : 'LAY', size };
}

/** Whole units with a space between thousands (non-breaking): 302 293, 1 302 212. */
export const spaced = v => Math.round(v).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ' ');

/** Compact volume: 950, 12.5k, 1.2m. */
export const short = v => (v >= 1e6 ? +(v / 1e6).toFixed(1) + 'm' : v >= 1e3 ? +(v / 1e3).toFixed(1) + 'k' : String(Math.round(v)));

/** Sum of 1/price over best prices, as a percentage; 0 when the book is incomplete. */
export function bookPct(book, back) {
  let sum = 0;
  for (const r of (book.runners ?? []).filter(r => r.status === 'ACTIVE')) {
    const best = (back ? r.ex?.availableToBack : r.ex?.availableToLay)?.[0];
    if (!best || !(best.price > 0)) return 0;
    sum += 1 / best.price;
  }
  return Math.round(sum * 1000) / 10;
}

/** Rolling liquidity history for one market: matched volume, money on the book, matched per minute. */
export class LiquidityTracker {
  constructor(max = 900, windowMs = 60000) { this.max = max; this.windowMs = windowMs; this.samples = []; this.last = null; }
  add(book, now = Date.now()) {
    let back = 0, lay = 0;
    for (const r of (book.runners ?? []).filter(r => r.status === 'ACTIVE')) {
      back += (r.ex?.availableToBack ?? []).reduce((s, p) => s + p.size, 0);
      lay += (r.ex?.availableToLay ?? []).reduce((s, p) => s + p.size, 0);
    }
    back = r2(back); lay = r2(lay);
    this.samples.push({ t: now, matched: book.totalMatched ?? 0, back, lay });
    if (this.samples.length > this.max) this.samples.shift();
    return this.last = { matched: book.totalMatched ?? 0, back, lay, backPct: bookPct(book, true), layPct: bookPct(book, false), perMin: this.rate(now) };
  }
  rate(now = Date.now()) {
    if (this.samples.length < 2) return 0;
    const last = this.samples.at(-1), start = this.samples.find(s => s.t >= now - this.windowMs) ?? this.samples[0];
    const mins = (last.t - start.t) / 60000;
    return mins <= 0 ? 0 : r2(Math.max(0, last.matched - start.matched) / mins);
  }
}
