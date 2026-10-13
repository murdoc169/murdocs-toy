// Live-score relay client for the standalone web server (the desktop app has its own in C#).
// The relay is a plain WebSocket that broadcasts JSON: { type: 'snapshot', matches: [...] } and { type: 'goal' | 'var', ... }.

export class LiveFeed {
  matches = [];
  alerts = [];
  connected = false;
  url = null;
  #ws = null; #stopped = false;

  start(url) {
    this.url = url; this.#stopped = false;
    const connect = (delay) => {
      if (this.#stopped) return;
      let ws;
      try { ws = new WebSocket(url); } catch { return setTimeout(() => connect(Math.min(delay * 2, 30)), delay * 1000); }
      this.#ws = ws;
      ws.onopen = () => { this.connected = true; delay = 2; };
      ws.onmessage = e => this.handle(String(e.data));
      ws.onclose = () => { this.connected = false; if (!this.#stopped) setTimeout(() => connect(Math.min(delay * 2, 30)), delay * 1000); };
      ws.onerror = () => { /* onclose follows and schedules the retry */ };
    };
    connect(2);
  }

  stop() { this.#stopped = true; this.connected = false; try { this.#ws?.close(); } catch { /* already closed */ } }

  #toMatch(m) {
    if (!m || typeof m !== 'object' || typeof m.home_team !== 'string' || typeof m.away_team !== 'string') return null;
    const n = v => (Number.isFinite(v) ? v : 0);
    return { home: m.home_team, away: m.away_team, homeScore: n(m.home_score), awayScore: n(m.away_score) };
  }

  /** Applies one relay message (public so it can be tested without a socket). */
  handle(json) {
    let msg;
    try { msg = JSON.parse(json); } catch { return; }
    if (msg?.type === 'snapshot' && Array.isArray(msg.matches)) { this.matches = msg.matches.map(m => this.#toMatch(m)).filter(Boolean); return; }
    const match = this.#toMatch(msg);
    if (match) {
      const i = this.matches.findIndex(x => x.home === match.home && x.away === match.away);
      if (i >= 0) this.matches[i] = match; else this.matches.push(match);
    }
    if (msg?.type === 'goal' || msg?.type === 'var') {
      const scorer = msg.scoring_team === 'home' ? match?.home : msg.scoring_team === 'away' ? match?.away : null;
      const text = match
        ? `${msg.type.toUpperCase()}  ${match.home} ${match.homeScore}-${match.awayScore} ${match.away}${scorer ? `  (${scorer})` : ''}`
        : `${msg.type.toUpperCase()}  ${json}`;
      this.alerts.push({ time: new Date().toISOString(), kind: msg.type, text });
      if (this.alerts.length > 200) this.alerts.shift();
    }
  }

  snapshot() { return { connected: this.connected, matches: this.matches, alerts: this.alerts.slice(-20) }; }
}
