# Polymarket edition (scaffold)

A ladder trading client for Polymarket in the style of Murdoc's Toy. This is the plan and what is already built.

## How Polymarket maps onto the ladder

| Betfair | Polymarket |
|---|---|
| Event > market > runner | Event > market (a question) > outcome (Yes / No), each outcome is its own order book |
| Odds 1.01 to 1000, uneven ticks | Price 0 to 1 (a probability), **one tick per market** (0.1, 0.01, 0.001 or 0.0001) |
| Back / lay | Buy / sell a share. A share pays 1 USDC if the outcome wins, else 0 |
| Liability on a lay | Selling at 0.40 risks 0.60 per share, buying at 0.40 risks 0.40 |
| Green up | Sell what you hold, or buy the opposite outcome |
| Minimum bet | `orderMinSize` per market (usually 5 shares) |
| Currency | USDC on Polygon |

Yes and No are two books of the same market, so a "Yes" ladder and a "No" ladder mirror each other (a Yes bid at 0.40 is a No ask at 0.60). The ladder can show one outcome and offer a flip.

## Built so far: `src/MurdocsToy.Polymarket`

Everything here is read-only and needs **no account, wallet or key**.

- `PolymarketClient`: events (by tag, search, slug) from the Gamma API, order books and tick size from the CLOB API, and the geoblock check.
- `MarketStream`: live books over the public market WebSocket (full book, then level updates, tick size changes, reconnect).
- `PriceGrid`: tick snapping, ladder rows, cents display, odds, cost and profit.
- Tests: offline parsers and stream, plus one live test (`MT_LIVE_POLYMARKET=1 dotnet test --filter PolymarketLive`), which passed.

## Not built yet

1. **Desktop shell**: navigator (Sport/Politics/Crypto... > event > market) and a ladder window reusing the Classic look. The Classic ladder control is tied to Betfair's models, so it needs a small interface (rows of price, size per side, own orders) rather than a copy.
2. **Trading**: see below. Nothing here can place an order yet.
3. **Positions, open orders and balance** (needs the wallet address; reading positions is public via the data API).
4. Automation rules (goal and suspension rules do not carry over directly; Polymarket sports markets do not suspend the same way).

## Trading: what it takes

Orders are signed, not logged in:

1. A wallet that holds USDC on Polygon. Most accounts created on polymarket.com use a **proxy wallet** (a contract) controlled by the key behind the login. Orders are signed by that key and name the proxy as the funder.
2. **L1 step**: sign an EIP-712 message once to create or derive API credentials (key, secret, passphrase).
3. **L2 step**: every request carries an HMAC signature made with those credentials.
4. **Each order** is an EIP-712 typed message signed by the private key (price, size, side, token, expiry, nonce, fee rate).
5. One-time on-chain approvals (USDC and conditional tokens) if the wallet has never traded.

The signing code can be written in C# with an Ethereum library such as Nethereum, or using Polymarket's official client libraries as a reference. The private key would be stored the way the Betfair password is (Windows DPAPI) and never leave the machine.

## What I need from you

1. **Where you are allowed to trade from.** Polymarket's location check says `blocked: true` from this machine right now (country IE, which looks like a VPN or proxy exit; your real country may differ). Reading prices works regardless, but placing orders from a blocked country breaks Polymarket's terms. Tell me your country and whether you trade through a VPN; I will not build around the block.
2. **Do you already have a funded Polymarket account?** If yes, which kind: email or social login (Magic, proxy wallet) or a browser wallet such as MetaMask. This decides the signing flow.
3. **How the key should be provided**: paste the private key into the app (stored with DPAPI), or use a separate wallet funded with only the amount you want to risk (strongly recommended).
4. **Scope of the first version**: read-only viewer first (safe, can be finished without any key), then trading. Which markets matter most to you: sports, politics, crypto?
5. Name and whether it lives in this repo (as a second app) or in its own repository.
