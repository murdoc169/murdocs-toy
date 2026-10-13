import { short } from './ladder.js';

const spark = (ctx, x, y, w, h, vals, colour, label) => {
  ctx.fillStyle = '#8b95a5'; ctx.font = '10px system-ui'; ctx.fillText(label, x, y - 3);
  ctx.strokeStyle = '#374150'; ctx.lineWidth = 1; ctx.strokeRect(x + .5, y + .5, w - 1, h - 1);
  if (vals.length < 2) return;
  const min = Math.min(...vals), max = Math.max(...vals), span = max - min;
  ctx.strokeStyle = colour; ctx.lineWidth = 1.6; ctx.beginPath();
  vals.forEach((v, i) => {
    const px = x + (i / (vals.length - 1)) * w;
    const py = span < 1e-9 ? y + h / 2 : y + h - 1 - ((v - min) / span) * (h - 2);
    i ? ctx.lineTo(px, py) : ctx.moveTo(px, py);
  });
  ctx.stroke();
};

/** Draws the liquidity tracker (numbers + two history charts) for one market into `box`. */
export function drawLiquidity(box, tracker) {
  let c = box.querySelector('canvas');
  if (!c) { c = document.createElement('canvas'); c.style.cssText = 'width:100%;height:96px;display:block'; box.replaceChildren(c); }
  const dpr = devicePixelRatio || 1, W = box.clientWidth, H = 96;
  c.width = W * dpr; c.height = H * dpr;
  const ctx = c.getContext('2d'); ctx.scale(dpr, dpr);
  ctx.fillStyle = '#1b2028'; ctx.fillRect(0, 0, W, H);
  ctx.fillStyle = '#8b95a5'; ctx.font = 'bold 10px system-ui'; ctx.fillText('LIQUIDITY', 8, 13);
  const s = tracker.last;
  if (!s) { ctx.font = '12px system-ui'; ctx.fillText('waiting for data...', 8, 34); return; }
  ctx.fillStyle = '#fff'; ctx.font = 'bold 15px system-ui'; ctx.fillText('Matched ' + short(s.matched), 8, 34);
  ctx.fillStyle = s.perMin > 0 ? '#7fe0a0' : '#8b95a5'; ctx.font = '12px system-ui';
  ctx.fillText((s.perMin > 0 ? '+' : '') + short(s.perMin) + '/min', 10, 52);
  ctx.fillStyle = '#d0d4dc';
  ctx.fillText(`Available   Back ${short(s.back)}   Lay ${short(s.lay)}`, 8, 70);
  ctx.fillText(`Book   Back ${s.backPct ? s.backPct.toFixed(1) + '%' : '-'}   Lay ${s.layPct ? s.layPct.toFixed(1) + '%' : '-'}`, 8, 86);
  const x0 = 250, w = W - x0 - 10;
  if (w < 60) return;
  spark(ctx, x0, 24, w, 28, tracker.samples.map(v => v.matched), '#5aa9e6', 'matched');
  spark(ctx, x0, 64, w, 28, tracker.samples.map(v => v.back + v.lay), '#4cc38a', 'available');
}
