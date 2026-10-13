# OpenLadder

An open-source Betfair Exchange trading client with two front ends:

- **Desktop** (`src/OpenLadder.Desktop`) – C# / WinForms: market browser, runner grid with 3-deep back/lay, price ladder with click-to-bet, current orders, balance.
- **Web** (`web/`) – dependency-free Node server plus a dark single-page UI with the same features and a cleaner layout.

Both use only Betfair's public, documented [Exchange API-NG](https://betfair-developer-docs.atlassian.net/wiki/spaces/1smk3cen4v3lu3yomq5qye0ni/overview). This is an independent, clean-room project. It contains no code from any commercial trading product and is not affiliated with Betfair.

## You need

- A Betfair account and an **application key** ([create one](https://apps.betfair.com/visualisers/api-ng-account-operations/) with *Create Developer App Keys*).
  - A **delayed** key lets you browse markets, prices and the ladder (data is delayed). **Placing and cancelling bets needs a live key**, which Betfair activates on request.
- .NET 8 SDK (desktop, tests) and/or Node 20+ (web).

## Configure

Credentials are read from environment variables (or typed into the login form). Never commit them: `.env*` is gitignored.

```
BETFAIR_USERNAME=...
BETFAIR_PASSWORD=...
BETFAIR_APP_KEY=...
```

## Run

```bash
# web UI -> http://127.0.0.1:8080  (binds to localhost only)
cd web && node server.mjs

# desktop UI (Windows)
dotnet run --project src/OpenLadder.Desktop
```

## Test

```bash
dotnet test                 # ladder unit tests + live API test when BETFAIR_* are set
cd web && node --test test.mjs
```

The desktop app also has `--smoke`, which logs in, loads a market and writes `smoke.txt` next to the executable.

## Status

Verified against the live API with a delayed key: login, event types, market catalogue, market book with depth and traded volume, account funds, current orders. Order placement and cancellation are implemented from the API spec and pass input validation, but have **not** been exercised live because that requires a live app key.

## Safety

Betting carries financial risk. Orders require a confirmation click, but this software is provided as-is with no warranty (see `LICENSE`). Test with small stakes.

## License

MIT
