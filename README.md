# Murdoc's Toy

An open-source Betfair Exchange trading client with two front ends:

- **Desktop** (`src/MurdocsToy.Desktop`) – C# / WinForms: market browser, runner grid with 3-deep back/lay, price ladder with click-to-bet, current orders, balance.
- **Web** (`web/`) – dependency-free Node server plus a dark single-page UI with the same features and a cleaner layout.

Both use only Betfair's public, documented [Exchange API-NG](https://betfair-developer-docs.atlassian.net/wiki/spaces/1smk3cen4v3lu3yomq5qye0ni/overview). This is an independent, clean-room project. It contains no code from any commercial trading product and is not affiliated with Betfair.

## Features

- **Tree explorer**: sport > competition > match > markets. Children load when you expand a node, so a match's dozens of markets stay out of the way. Search finds matches by name; the *In-play* box filters to live games.
- **Pop-up ladders**: double-click a match (or press *Open ladder*) and its Match Odds open in their own window with one ladder per runner side by side (Home / Draw / Away). Open as many as you like.
- **One-click betting**: click the blue *Back* or pink *Lay* column at a price to bet your selected stake. Click the orders column to cancel your unmatched bets at that price. *Cancel all* clears the market.
- **Custom stakes and options**: stake buttons and the stake box are yours to edit (*Options*): stake presets, default stake, row height, font size, refresh rate, back/lay colours, bet persistence, one-click or confirm-every-bet, always on top, auto-centre.
- **Position and green up**: each ladder shows your profit if that runner wins or loses and a *Green up* button that places the hedge bet locking the same profit either way.
- **Liquidity tracker**: every market you open (main window, each pop-up ladder, and the browser) shows matched volume and how fast it is growing per minute, the money available to back and to lay, the back/lay book percentages, and history charts. The tree also shows each market's matched volume.
- **Remembered login**: tick *Remember me* once. The password is stored encrypted with Windows DPAPI (only your Windows account can decrypt it) and the app logs in automatically from then on; *Options > Forget saved login* removes it. Expired sessions renew themselves.
- **Mirror to web**: while the desktop app runs it also serves the web UI at **http://localhost:8080** (next free port if busy), using the desktop's Betfair session. The browser needs no login, follows the market you select in the desktop app, lists the desktop's open ladders, and uses your stake buttons. Bets from the browser go through the same session. It listens on this machine only, and POSTs need a custom header so other websites cannot place bets. Turn it off in *Options*.
- **Live scores**: point *Options > Live scores relay* at a WebSocket relay that broadcasts JSON `snapshot`, `goal` and `var` messages (`home_team`, `away_team`, `home_score`, `away_score`, optional `scoring_team`); the standalone web server reads `MT_RELAY_URL`. Matches are paired with Betfair events by team name (accents, "Utd"/"United", FC/SC prefixes and youth/women/reserve squads are handled), and the game shows as `Porto 0 - 1 Benfica (145643€)`: live score between the teams, the match's matched volume after. Goals and VAR changes flash the ladder and the main view.
- **Favorites**: select a match and press **★ Favorite** (or right-click it; in the browser press the ☆ next to it). Favorites live in a **★ Favorites** section at the top of the tree, with scores and volume. Tick **Auto-open favorites on score change** and the ladder of any favorite whose score changes opens by itself (or is brought to the front if already open), including a goal being taken away.
- **Privacy mode**: `--privacy` hides balance and orders (for screenshots or screen sharing).

## Screenshots

**Desktop**: tree explorer, runner grid and ladder.

![Murdoc's Toy desktop app with tree explorer](docs/desktop.png)

**Pop-up ladder** (desktop): Home / Draw / Away ladders, stake buttons, green up.

![Murdoc's Toy pop-up ladder](docs/ladder.png)

**Web UI**: same tree explorer in the browser.

![Murdoc's Toy web UI](docs/web.png)

**Web pop-up ladder**: opened by double-clicking a match.

![Murdoc's Toy web pop-up ladder](docs/web-ladder.png)

**Browser mirroring the desktop app**

![Murdoc's Toy web mirror](docs/web-mirror.png)

*The screenshots use a delayed Betfair key (data is delayed) and show public market data only.*

## You need

- A Betfair account and an **application key** ([create one](https://apps.betfair.com/visualisers/api-ng-account-operations/) with *Create Developer App Keys*).
  - A **delayed** key lets you browse markets, prices and the ladder (data is delayed). **Placing and cancelling bets needs a live key**, which Betfair activates on request.
- .NET 8 SDK (desktop, tests) and/or Node 20+ (web).

## Install (Windows)

Download `MurdocsToy-Setup-0.3.0.exe` from the [Releases](../../releases) page and run it. It needs no .NET install, adds a Start Menu entry and an optional desktop shortcut, and can be removed from *Apps & features*. Windows SmartScreen may warn because the installer is unsigned.

On first launch enter your Betfair username, password and application key. The username and key are remembered in `%APPDATA%\MurdocsToy\settings.json`; the password is never stored.

To build the installer yourself:

```bash
dotnet publish src/MurdocsToy.Desktop -c Release -r win-x64 --self-contained true   -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/app
ISCC.exe installer/MurdocsToy.iss      # needs Inno Setup 6 -> dist/MurdocsToy-Setup-0.3.0.exe
```

## Configure

Desktop: type them into the login form once (see *Remembered login*). Standalone web server: environment variables or a `.env.local` file next to `web/` or in the repo root (loaded automatically; gitignored), or the browser login form, which remembers your username and app key. Never commit credentials: `.env*` is gitignored.

```
BETFAIR_USERNAME=...
BETFAIR_PASSWORD=...
BETFAIR_APP_KEY=...
```

## Run from source

```bash
# standalone web UI -> http://localhost:8080  (not needed while the desktop app is running; it serves the same page)
cd web && node server.mjs

# desktop UI (Windows)
dotnet run --project src/MurdocsToy.Desktop
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
