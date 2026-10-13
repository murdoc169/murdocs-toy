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
    }

    public async Task KeepAliveAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, KeepAliveUrl);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("X-Application", _appKey);
        req.Headers.Add("X-Authentication", _session);
        await _http.SendAsync(req, ct);
    }

    async Task<T> CallAsync<T>(string url, string method, object args, CancellationToken ct)
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

    public async Task<List<CurrentOrder>> ListCurrentOrdersAsync(CancellationToken ct = default)
    {
        var r = await Betting<JsonNode>("listCurrentOrders", new { orderProjection = "ALL" }, ct);
        return (r["currentOrders"]?.AsArray() ?? new JsonArray()).Select(o => new CurrentOrder(
            o!["betId"]!.ToString(), o["marketId"]!.ToString(), o["selectionId"]!.GetValue<long>(),
            o["side"]!.ToString(), o["status"]!.ToString(), o["priceSize"]!["price"]!.GetValue<double>(),
            o["priceSize"]!["size"]!.GetValue<double>(), o["sizeMatched"]?.GetValue<double>() ?? 0,
            o["sizeRemaining"]?.GetValue<double>() ?? 0)).ToList();
    }

    public async Task<AccountFunds> GetAccountFundsAsync(CancellationToken ct = default)
    {
        var r = await CallAsync<JsonNode>(AccountUrl, "AccountAPING/v1.0/getAccountFunds", new { }, ct);
        return new AccountFunds(r["availableToBetBalance"]?.GetValue<double>() ?? 0, r["exposure"]?.GetValue<double>() ?? 0);
    }

    public void Dispose() => _http.Dispose();
}
