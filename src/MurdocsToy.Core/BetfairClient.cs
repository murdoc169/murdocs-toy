using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MurdocsToy.Core;

/// <summary>Minimal client for the public Betfair Exchange API-NG (interactive login + JSON-RPC).</summary>
public sealed class BetfairClient : IDisposable
{
    const string LoginUrl = "https://identitysso.betfair.com/api/login";
    const string KeepAliveUrl = "https://identitysso.betfair.com/api/keepAlive";
    const string BettingUrl = "https://api.betfair.com/exchange/betting/json-rpc/v1";
    const string AccountUrl = "https://api.betfair.com/exchange/account/json-rpc/v1";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly string _appKey;
    string? _session;
    string? _user, _pass;   // kept in memory only, to renew an expired session

    public BetfairClient(string appKey) => _appKey = appKey;

    public bool IsLoggedIn => _session != null;

    public async Task LoginAsync(string username, string password, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, LoginUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            { ["username"] = username, ["password"] = password }),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("X-Application", _appKey);
        using var res = await _http.SendAsync(req, ct);
        var body = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))!;
        if (body["status"]?.GetValue<string>() != "SUCCESS")
            throw new BetfairException(body["error"]?.GetValue<string>() ?? "LOGIN_FAILED", "Betfair login failed: " + body["error"]);
        _session = body["token"]!.GetValue<string>();
        _user = username; _pass = password;
    }

    public async Task KeepAliveAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, KeepAliveUrl);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("X-Application", _appKey);
        req.Headers.Add("X-Authentication", _session);
        await _http.SendAsync(req, ct);
    }

    static readonly HashSet<string> SessionErrors = new() { "INVALID_SESSION_INFORMATION", "NO_SESSION", "NO_APP_KEY_SESSION" };

    /// <summary>Calls the API; if Betfair says the session expired, logs in again once and retries.</summary>
    async Task<T> CallAsync<T>(string url, string method, object args, CancellationToken ct)
    {
        try { return await CallOnce<T>(url, method, args, ct); }
        catch (BetfairException ex) when (SessionErrors.Contains(ex.Code) && _user != null && _pass != null)
        {
            _session = null;
            await LoginAsync(_user, _pass, ct);
            return await CallOnce<T>(url, method, args, ct);
        }
    }

    async Task<T> CallOnce<T>(string url, string method, object args, CancellationToken ct)
    {
        if (_session == null) throw new InvalidOperationException("Not logged in.");
        var payload = JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = args, id = 1 }, Json);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        req.Headers.Add("X-Application", _appKey);
        req.Headers.Add("X-Authentication", _session);
        using var res = await _http.SendAsync(req, ct);
        var node = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))!;
        if (node["error"] is JsonNode err)
        {
            var code = err["data"]?["APINGException"]?["errorCode"]?.GetValue<string>()
                    ?? err["data"]?["AccountAPINGException"]?["errorCode"]?.GetValue<string>()
                    ?? err["message"]?.ToString() ?? "API_ERROR";
            throw new BetfairException(code, $"{method} failed: {code}");
        }
        return node["result"]!.Deserialize<T>(Json)!;
    }

    Task<T> Betting<T>(string op, object args, CancellationToken ct) =>
        CallAsync<T>(BettingUrl, "SportsAPING/v1.0/" + op, args, ct);

    public async Task<List<EventType>> ListEventTypesAsync(CancellationToken ct = default)
    {
        var r = await Betting<List<JsonNode>>("listEventTypes", new { filter = new { } }, ct);
        return r.Select(x => new EventType(x["eventType"]!["id"]!.ToString(), x["eventType"]!["name"]!.ToString()))
                .OrderBy(e => e.Name).ToList();
    }

    static Dictionary<string, object?> TreeFilter(string eventTypeId, bool inPlayOnly, string? competitionId = null, string? text = null, string? eventId = null, DateTime? from = null, DateTime? to = null)
    {
        var f = new Dictionary<string, object?>
        {
            ["eventTypeIds"] = new[] { eventTypeId },
            ["inPlayOnly"] = inPlayOnly ? true : null,
            ["competitionIds"] = competitionId == null ? null : new[] { competitionId },
            ["eventIds"] = eventId == null ? null : new[] { eventId },
            ["textQuery"] = text,
        };
        // In-play games can have started long ago (tennis, cricket, golf), so in-play searches have no start-time limit.
        // Otherwise: games that started up to 3h ago through the next 2 days.
        if (!inPlayOnly) f["marketStartTime"] = new { from = (from ?? DateTime.UtcNow.AddHours(-3)).ToString("o"), to = (to ?? DateTime.UtcNow.AddDays(2)).ToString("o") };
        return f;
    }

    public async Task<List<CompetitionNode>> ListCompetitionsAsync(string eventTypeId, bool inPlayOnly = false, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var r = await Betting<List<JsonNode>>("listCompetitions", new { filter = TreeFilter(eventTypeId, inPlayOnly, from: from, to: to) }, ct);
        return r.Select(x => new CompetitionNode(x["competition"]!["id"]!.ToString(), x["competition"]!["name"]!.ToString(),
                x["competitionRegion"]?.ToString(), x["marketCount"]?.GetValue<int>() ?? 0))
            .OrderBy(c => c.Name).ToList();
    }

    public async Task<List<EventNode>> ListEventsAsync(string eventTypeId, string? competitionId = null, string? text = null,
        bool inPlayOnly = false, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var r = await Betting<List<JsonNode>>("listEvents", new { filter = TreeFilter(eventTypeId, inPlayOnly, competitionId, text, null, from, to) }, ct);
        return r.Select(x => new EventNode(x["event"]!["id"]!.ToString(), x["event"]!["name"]!.ToString(), x["event"]!["countryCode"]?.ToString(),
                x["event"]!["openDate"]?.GetValue<DateTime>(), x["marketCount"]?.GetValue<int>() ?? 0))
            .OrderBy(e => e.OpenDate).ThenBy(e => e.Name).ToList();
    }

    /// <summary>
    /// Matched volume per match: the largest market of each event (normally Match Odds). Events are queried in batches,
    /// so a whole tree level costs a few calls. Events without markets are left out.
    /// </summary>
    public async Task<Dictionary<string, double>> GetEventVolumesAsync(IEnumerable<string> eventIds, CancellationToken ct = default)
    {
        var result = new Dictionary<string, double>();
        foreach (var chunk in eventIds.Distinct().Chunk(20))
        {
            var r = await Betting<List<MarketCatalogEntry>>("listMarketCatalogue", new
            {
                filter = new { eventIds = chunk },
                marketProjection = new[] { "EVENT" },
                sort = "MAXIMUM_TRADED",
                maxResults = 1000,
            }, ct);
            foreach (var m in r)
                if (m.Event != null && (m.TotalMatched ?? 0) > result.GetValueOrDefault(m.Event.Id))
                    result[m.Event.Id] = m.TotalMatched!.Value;
        }
        return result;
    }

    public async Task<MarketCatalogEntry?> GetMarketAsync(string marketId, CancellationToken ct = default)
    {
        var r = await Betting<List<MarketCatalogEntry>>("listMarketCatalogue", new
        {
            filter = new { marketIds = new[] { marketId } },
            marketProjection = new[] { "EVENT", "MARKET_START_TIME", "RUNNER_DESCRIPTION" },
            maxResults = 1,
        }, ct);
        return r.FirstOrDefault();
    }

    /// <summary>All markets of one match/event; "Match Odds" first, then by traded volume.</summary>
    public async Task<List<MarketCatalogEntry>> ListEventMarketsAsync(string eventId, CancellationToken ct = default)
    {
        var r = await Betting<List<MarketCatalogEntry>>("listMarketCatalogue", new
        {
            filter = new { eventIds = new[] { eventId } },
            marketProjection = new[] { "EVENT", "MARKET_START_TIME", "RUNNER_DESCRIPTION" },
            sort = "MAXIMUM_TRADED",
            maxResults = 200,
        }, ct);
        return r.OrderBy(m => m.MarketName == "Match Odds" ? 0 : 1).ToList();
    }

    public async Task<List<MarketCatalogEntry>> ListMarketCatalogueAsync(
        string? eventTypeId = null, string? textQuery = null, bool inPlayOnly = false,
        string? marketTypeCode = null, int max = 100, CancellationToken ct = default)
    {
        var filter = new Dictionary<string, object?>
        {
            ["eventTypeIds"] = eventTypeId == null ? null : new[] { eventTypeId },
            ["textQuery"] = textQuery,
            ["inPlayOnly"] = inPlayOnly ? true : null,
            ["marketTypeCodes"] = marketTypeCode == null ? null : new[] { marketTypeCode },
            ["marketStartTime"] = new { from = DateTime.UtcNow.AddHours(-3).ToString("o") },
        };
        return await Betting<List<MarketCatalogEntry>>("listMarketCatalogue", new
        {
            filter,
            marketProjection = new[] { "EVENT", "MARKET_START_TIME", "RUNNER_DESCRIPTION" },
            sort = "FIRST_TO_START",
            maxResults = max,
        }, ct);
    }

    public async Task<List<MarketBook>> ListMarketBookAsync(IEnumerable<string> marketIds, CancellationToken ct = default) =>
        await Betting<List<MarketBook>>("listMarketBook", new
        {
            marketIds = marketIds.ToArray(),
            priceProjection = new { priceData = new[] { "EX_ALL_OFFERS", "EX_TRADED" } },
        }, ct);

    public async Task<PlaceReport> PlaceOrderAsync(string marketId, PlaceInstruction i, CancellationToken ct = default)
    {
        var r = await Betting<JsonNode>("placeOrders", new
        {
            marketId,
            instructions = new[]
            {
                new
                {
                    selectionId = i.SelectionId, side = i.Side, orderType = "LIMIT",
                    limitOrder = new { size = i.Size, price = i.Price, persistenceType = i.PersistenceType },
                },
            },
        }, ct);
        var rep = r["instructionReports"]?[0];
        return new PlaceReport(r["status"]!.ToString(), r["errorCode"]?.ToString() ?? rep?["errorCode"]?.ToString(),
            rep?["betId"]?.ToString(), rep?["sizeMatched"]?.GetValue<double>());
    }

    public async Task CancelOrderAsync(string marketId, string betId, CancellationToken ct = default) =>
        await Betting<JsonNode>("cancelOrders", new { marketId, instructions = new[] { new { betId } } }, ct);

    public async Task<List<CurrentOrder>> ListCurrentOrdersAsync(string? marketId = null, CancellationToken ct = default)
    {
        var r = await Betting<JsonNode>("listCurrentOrders", new { orderProjection = "ALL", marketIds = marketId == null ? null : new[] { marketId } }, ct);
        return (r["currentOrders"]?.AsArray() ?? new JsonArray()).Select(o => new CurrentOrder(
            o!["betId"]!.ToString(), o["marketId"]!.ToString(), o["selectionId"]!.GetValue<long>(),
            o["side"]!.ToString(), o["status"]!.ToString(), o["priceSize"]!["price"]!.GetValue<double>(),
            o["priceSize"]!["size"]!.GetValue<double>(), o["sizeMatched"]?.GetValue<double>() ?? 0,
            o["sizeRemaining"]?.GetValue<double>() ?? 0, o["averagePriceMatched"]?.GetValue<double>() ?? 0)).ToList();
    }

    public async Task<AccountDetails> GetAccountDetailsAsync(CancellationToken ct = default)
    {
        var r = await CallAsync<JsonNode>(AccountUrl, "AccountAPING/v1.0/getAccountDetails", new { }, ct);
        return new AccountDetails(r["currencyCode"]?.ToString() ?? "EUR", r["countryCode"]?.ToString());
    }

    public async Task<AccountFunds> GetAccountFundsAsync(CancellationToken ct = default)
    {
        var r = await CallAsync<JsonNode>(AccountUrl, "AccountAPING/v1.0/getAccountFunds", new { }, ct);
        return new AccountFunds(r["availableToBetBalance"]?.GetValue<double>() ?? 0, r["exposure"]?.GetValue<double>() ?? 0);
    }

    public void Dispose() => _http.Dispose();
}
