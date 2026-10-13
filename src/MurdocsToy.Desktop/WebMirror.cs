using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>What the desktop app is showing right now; the browser mirrors it.</summary>
static class MirrorState
{
    static readonly object Gate = new();
    static readonly Dictionary<string, string> Ladders = new();   // marketId -> title
    public static string? SelectedMarketId { get; private set; }
    public static int Version { get; private set; }

    public static void Select(string? marketId) { lock (Gate) { SelectedMarketId = marketId; Version++; } }
    public static void LadderOpened(string marketId, string title) { lock (Gate) { Ladders[marketId] = title; Version++; } }
    public static void LadderClosed(string marketId) { lock (Gate) { Ladders.Remove(marketId); Version++; } }
    public static object Snapshot() { lock (Gate) return new { selectedMarketId = SelectedMarketId, version = Version, ladders = Ladders.Select(l => new { marketId = l.Key, name = l.Value }).ToList() }; }
}

/// <summary>
/// Serves the web UI from inside the desktop app, on localhost only. It uses the desktop's Betfair session, so the
/// browser needs no separate login and shows the same market, ladders and stake settings.
/// </summary>
sealed class WebMirror : IDisposable
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    readonly BetfairClient _bf;
    readonly Options _opt;
    readonly bool _privacy;
    readonly HttpListener _http = new();
    CancellationTokenSource? _cts;
    public int Port { get; private set; }
    public string Url => $"http://localhost:{Port}/";

    readonly LiveScoreFeed? _feed;
    public WebMirror(BetfairClient bf, Options opt, bool privacy, LiveScoreFeed? feed = null) { _bf = bf; _opt = opt; _privacy = privacy; _feed = feed; }

    /// <summary>Starts on the first free port from 8080; returns false if none could be bound.</summary>
    public bool Start()
    {
        for (int port = 8080; port < 8090; port++)
        {
            try
            {
                var l = new HttpListener();
                l.Prefixes.Add($"http://127.0.0.1:{port}/");
                l.Prefixes.Add($"http://localhost:{port}/");
                l.Start();
                _listener = l; Port = port; break;
            }
            catch (HttpListenerException) { }
        }
        if (_listener == null) return false;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => Loop(_cts.Token));
        return true;
    }

    HttpListener? _listener;

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener!.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); } catch { break; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    static void Send(HttpListenerContext c, int code, string body, string type = "application/json; charset=utf-8")
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        Send(c, code, bytes, type);
    }

    static void Send(HttpListenerContext c, int code, byte[] bytes, string type)
    {
        try
        {
            c.Response.StatusCode = code; c.Response.ContentType = type;
            c.Response.Headers["Cache-Control"] = "no-store";
            c.Response.ContentLength64 = bytes.Length;
            c.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        finally { c.Response.Close(); }
    }

    static string Ctype(string file) => Path.GetExtension(file) switch
    {
        ".html" => "text/html; charset=utf-8", ".js" => "text/javascript; charset=utf-8", ".css" => "text/css", _ => "application/octet-stream",
    };

    async Task Handle(HttpListenerContext c)
    {
        var req = c.Request;
        try
        {
            // Only the local machine, and only the names we serve (blocks DNS-rebinding).
            var host = req.UserHostName?.Split(':')[0];
            if (host is not ("localhost" or "127.0.0.1")) { Send(c, 403, "forbidden", "text/plain"); return; }
            var path = req.Url!.AbsolutePath;
            if (path.StartsWith("/api/"))
            {
                // POSTs need a custom header: a page on another site cannot send it without a CORS preflight, which we never allow.
                if (req.HttpMethod == "POST" && req.Headers["X-MT"] != "1") { Send(c, 403, Err("FORBIDDEN")); return; }
                try { Send(c, 200, JsonSerializer.Serialize(await Api(req, path), Json)); }
                catch (BetfairException ex) { Send(c, 400, Err(ex.Code, ex.Message)); }
                return;
            }
            var file = path == "/" ? "index.html" : path.TrimStart('/');
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("web/" + file);
            if (s == null || file.Contains("..")) { Send(c, 404, "not found", "text/plain"); return; }
            using var ms = new MemoryStream(); s.CopyTo(ms);
            Send(c, 200, ms.ToArray(), Ctype(file));
        }
        catch (Exception ex) { try { Send(c, 500, Err("ERROR", ex.Message)); } catch { } }
    }

    static string Err(string code, string? message = null) => JsonSerializer.Serialize(new { error = code, message = message ?? code }, Json);

    static async Task<T> Body<T>(HttpListenerRequest r)
    {
        using var rd = new StreamReader(r.InputStream, Encoding.UTF8);
        var s = await rd.ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(string.IsNullOrWhiteSpace(s) ? "{}" : s, Json)!;
    }

    record PlaceBody(string MarketId, long SelectionId, string Side, double Price, double Size, string? Persistence);
    record CancelBody(string MarketId, string BetId);
    record FavBody(string EventId, string Name, bool Favorite);

    async Task<object?> Api(HttpListenerRequest r, string path)
    {
        var q = r.QueryString;
        string? Q(string k) => string.IsNullOrEmpty(q[k]) ? null : q[k];
        bool inPlay = q["inPlay"] == "1";
        switch (r.HttpMethod + " " + path)
        {
            case "GET /api/status": return new { loggedIn = true, hasKey = true, hasEnvCreds = true, mirror = true };
            case "GET /api/mirror": return MirrorState.Snapshot();
            case "GET /api/volumes": return await _bf.GetEventVolumesAsync((Q("ids") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));
            case "GET /api/favorites": return _opt.Favorites.Select(f => new { eventId = f.EventId, f.Name });
            case "GET /api/live":
                return new
                {
                    connected = _feed?.Connected ?? false,
                    matches = (_feed?.Matches ?? Array.Empty<LiveMatch>()).Select(m => new { m.Home, m.Away, m.HomeScore, m.AwayScore }),
                    alerts = (_feed?.Alerts ?? Array.Empty<LiveAlert>()).TakeLast(20).Select(a => new { time = a.Time, a.Kind, a.Text }),
                };
            case "GET /api/options": return new { stakes = _opt.StakePresets, stake = _opt.DefaultStake, persistence = _opt.Persistence, confirm = _opt.ConfirmBets };
            case "GET /api/eventtypes": return await _bf.ListEventTypesAsync();
            case "GET /api/competitions": return await _bf.ListCompetitionsAsync(Q("eventTypeId")!, inPlay);
            case "GET /api/events": return await _bf.ListEventsAsync(Q("eventTypeId")!, Q("competitionId"), Q("q"), inPlay);
            case "GET /api/eventmarkets": return await _bf.ListEventMarketsAsync(Q("eventId")!);
            case "GET /api/market": return await _bf.GetMarketAsync(Q("marketId")!);
            case "GET /api/book": return (await _bf.ListMarketBookAsync(new[] { Q("marketId")! })).FirstOrDefault();
            case "GET /api/funds":
                if (_privacy) throw new BetfairException("PRIVACY", "hidden in privacy mode");
                var f = await _bf.GetAccountFundsAsync();
                return new { available = f.AvailableToBetBalance, exposure = f.Exposure };
            case "GET /api/orders":
                if (_privacy) return Array.Empty<object>();
                return (await _bf.ListCurrentOrdersAsync(Q("marketId"))).Select(o => new
                {
                    o.BetId, o.MarketId, o.SelectionId, o.Side, o.Status, o.Price, o.Size,
                    matched = o.SizeMatched, remaining = o.SizeRemaining, avgPrice = o.AveragePriceMatched,
                });
            case "POST /api/place":
                var p = await Body<PlaceBody>(r);
                if (p.Side is not ("BACK" or "LAY") || !(p.Price is >= 1.01 and <= 1000) || !(p.Size >= 1)
                    || (p.Persistence != null && p.Persistence is not ("LAPSE" or "PERSIST" or "MARKET_ON_CLOSE")))
                    throw new BetfairException("INVALID_INPUT", "invalid order");
                return await _bf.PlaceOrderAsync(p.MarketId, new PlaceInstruction(p.SelectionId, p.Side, p.Price, p.Size, p.Persistence ?? "LAPSE"));
            case "POST /api/favorites":
                var fb = await Body<FavBody>(r);
                if (string.IsNullOrWhiteSpace(fb.EventId) || string.IsNullOrWhiteSpace(fb.Name)) throw new BetfairException("INVALID_INPUT", "invalid favorite");
                lock (_opt)
                {
                    var list = _opt.Favorites.ToList();
                    list.RemoveAll(f => f.EventId == fb.EventId);
                    if (fb.Favorite) list.Add(new FavoriteEvent(fb.EventId, fb.Name));
                    _opt.Favorites = list; _opt.Save();
                }
                return new { ok = true };
            case "POST /api/cancel":
                var cb = await Body<CancelBody>(r);
                await _bf.CancelOrderAsync(cb.MarketId, cb.BetId);
                return new { ok = true };
            default: throw new BetfairException("NOT_FOUND", "unknown endpoint");
        }
    }

    public void Dispose() { _cts?.Cancel(); try { _listener?.Stop(); _listener?.Close(); } catch { } }
}
