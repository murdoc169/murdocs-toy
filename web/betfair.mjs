// Minimal Betfair Exchange API-NG client (interactive login + JSON-RPC). No dependencies.
const LOGIN = 'https://identitysso.betfair.com/api/login';
const BETTING = 'https://api.betfair.com/exchange/betting/json-rpc/v1';
const ACCOUNT = 'https://api.betfair.com/exchange/account/json-rpc/v1';

export class BetfairError extends Error {
  constructor(code, message) { super(message ?? code); this.code = code; }
}

export class Betfair {
  #appKey; #session = null;
  constructor(appKey) { this.#appKey = appKey; }
  get loggedIn() { return this.#session !== null; }

  async login(username, password) {
    const res = await fetch(LOGIN, {
      method: 'POST',
      headers: { Accept: 'application/json', 'X-Application': this.#appKey, 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({ username, password }),
    });
    const j = await res.json();
    if (j.status !== 'SUCCESS') throw new BetfairError(j.error || 'LOGIN_FAILED', 'Betfair login failed: ' + j.error);
    this.#session = j.token;
  }

  async #rpc(url, method, params) {
    if (!this.#session) throw new BetfairError('NOT_LOGGED_IN');
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Application': this.#appKey, 'X-Authentication': this.#session },
      body: JSON.stringify({ jsonrpc: '2.0', method, params, id: 1 }),
    });
    const j = await res.json();
    if (j.error) {
      const code = j.error.data?.APINGException?.errorCode ?? j.error.data?.AccountAPINGException?.errorCode ?? j.error.message;
      if (code === 'INVALID_SESSION_INFORMATION') this.#session = null;
      throw new BetfairError(code, `${method} failed: ${code}`);
    }
    return j.result;
  }
  #bet(op, params) { return this.#rpc(BETTING, `SportsAPING/v1.0/${op}`, params); }

  async eventTypes() {
    const r = await this.#bet('listEventTypes', { filter: {} });
    return r.map(x => ({ id: x.eventType.id, name: x.eventType.name })).sort((a, b) => a.name.localeCompare(b.name));
  }

  markets({ eventTypeId, text, inPlay, marketType, max = 100 }) {
    const filter = { marketStartTime: { from: new Date(Date.now() - 3 * 3600e3).toISOString() } };
    if (eventTypeId) filter.eventTypeIds = [eventTypeId];
    if (text) filter.textQuery = text;
    if (inPlay) filter.inPlayOnly = true;
    if (marketType) filter.marketTypeCodes = [marketType];
    return this.#bet('listMarketCatalogue', {
      filter, marketProjection: ['EVENT', 'MARKET_START_TIME', 'RUNNER_DESCRIPTION'], sort: 'FIRST_TO_START', maxResults: max,
    });
  }

  async book(marketId) {
    const r = await this.#bet('listMarketBook', { marketIds: [marketId], priceProjection: { priceData: ['EX_ALL_OFFERS', 'EX_TRADED'] } });
    return r[0] ?? null;
  }

  async place(marketId, { selectionId, side, price, size }) {
    const r = await this.#bet('placeOrders', {
      marketId,
      instructions: [{ selectionId, side, orderType: 'LIMIT', limitOrder: { size, price, persistenceType: 'LAPSE' } }],
    });
    const rep = r.instructionReports?.[0];
    return { status: r.status, errorCode: r.errorCode ?? rep?.errorCode, betId: rep?.betId, sizeMatched: rep?.sizeMatched };
  }

  cancel(marketId, betId) { return this.#bet('cancelOrders', { marketId, instructions: [{ betId }] }); }

  async orders() {
    const r = await this.#bet('listCurrentOrders', { orderProjection: 'ALL' });
    return r.currentOrders.map(o => ({
      betId: o.betId, marketId: o.marketId, selectionId: o.selectionId, side: o.side, status: o.status,
      price: o.priceSize.price, size: o.priceSize.size, matched: o.sizeMatched ?? 0, remaining: o.sizeRemaining ?? 0,
    }));
  }

  async funds() {
    const r = await this.#rpc(ACCOUNT, 'AccountAPING/v1.0/getAccountFunds', {});
    return { available: r.availableToBetBalance, exposure: r.exposure };
  }
}
