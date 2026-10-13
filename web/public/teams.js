// Matches live-score team names (SofaScore style) to Betfair event names ("Home v Away").
// Same rules as TeamMatcher in the C# core; both are tested with the same cases.
const STOP = new Set(['fc', 'cf', 'afc', 'sc', 'fk', 'sk', 'ac', 'as', 'cd', 'club', 'de', 'the', 'ud', 'sd', 'cs', 'if', 'bk', 'sv', 'fbc', 'ca', 'calcio']);
// words shared by many different clubs: they cannot decide a match on their own
const WEAK = new Set(['real', 'city', 'united', 'manchester', 'athletic', 'atletico', 'inter', 'sporting', 'dynamo', 'sport', 'town', 'rovers',
  'wanderers', 'olympic', 'saint', 'international', 'racing', 'union', 'borussia', 'north', 'south', 'east', 'west']);
const ALIAS = { utd: 'united', man: 'manchester', intl: 'international', st: 'saint', munchen: 'munich', wolves: 'wolverhampton' };
// squad markers: a youth/women/reserve side must only match the same kind of side
const MARKER = /^(u\d{2}|w|women|ladies|res|reserves|ii|iii|b)$/;
const MARKER_CLASS = { res: 'reserve', reserves: 'reserve', u21: 'reserve', u23: 'reserve', w: 'women', women: 'women', ladies: 'women' };

const fold = s => s.normalize('NFD').replace(/\p{Mn}/gu, '').replace(/đ/gi, 'd').toLowerCase();

export function tokens(name) {
  const words = new Set(), markers = new Set();
  for (const raw of fold(name).split(/[^a-z0-9]+/).filter(Boolean)) {
    const t = ALIAS[raw] ?? raw;
    if (MARKER.test(t)) markers.add(MARKER_CLASS[t] ?? t);
    else if (!STOP.has(t)) words.add(t);
  }
  return { words, markers };
}

const tokenEq = (a, b) => a === b || (Math.min(a.length, b.length) >= 4 && (a.startsWith(b) || b.startsWith(a)));

/** 0..1; 0 when the squad markers differ (Arsenal vs Arsenal U19). */
export function similarity(a, b) {
  const A = tokens(a), B = tokens(b);
  const sameMarkers = A.markers.size === B.markers.size && [...A.markers].every(m => B.markers.has(m));
  if (!sameMarkers || !A.words.size || !B.words.size) return 0;
  const hits = [...A.words].filter(x => [...B.words].some(y => tokenEq(x, y)));
  const min = Math.min(A.words.size, B.words.size);
  // a partial overlap ("Real Madrid" vs "Real Sociedad") needs at least one distinctive shared word
  if (hits.length < min && hits.every(h => WEAK.has(h))) return 0;
  return hits.length / min;
}

/** How well a live match fits a Betfair event name; 0 when it does not. */
export function matchScore(eventName, home, away) {
  const parts = eventName.split(/\s+v\s+/i);
  if (parts.length !== 2) return 0;
  const h = similarity(parts[0], home), a = similarity(parts[1], away);
  return Math.min(h, a) >= 0.4 && (h + a) / 2 >= 0.7 ? (h + a) / 2 : 0;
}

/** Best live match for an event name, or null. `matches` items: { home, away, homeScore, awayScore }. */
export function findLive(eventName, matches) {
  let best = null, bestScore = 0;
  for (const m of matches ?? []) { const s = matchScore(eventName, m.home, m.away); if (s > bestScore) { best = m; bestScore = s; } }
  return best;
}
