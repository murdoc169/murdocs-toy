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

/** Prices descending, `above` ticks over the centre and `below` under it. */
export function window(centre, above, below) {
  let p = snap(centre);
  for (let i = 0; i < above; i++) p = up(p);
  const out = [];
  for (let i = 0; i < above + below + 1; i++) { out.push(p); p = down(p); }
  return out;
}
