using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MurdocsToy.Core;

public record LiveMatch(long SofaEventId, string Home, string Away, int HomeScore, int AwayScore, DateTime Updated)
{
    public string ScoreText => $"{HomeScore}-{AwayScore}";
}

/// <summary>A goal or VAR event from the relay.</summary>
public record LiveAlert(DateTime Time, string Kind, string Text, string? Home, string? Away);

/// <summary>Matches live-score team names (SofaScore style) to Betfair event names ("Home v Away").</summary>
public static partial class TeamMatcher
{
    static readonly HashSet<string> Stop = new() { "fc", "cf", "afc", "sc", "fk", "sk", "ac", "as", "cd", "club", "de", "the", "ud", "sd", "cs", "if", "bk", "sv", "fbc", "ca", "calcio" };
    // words shared by many different clubs: they cannot decide a match on their own
    static readonly HashSet<string> Weak = new() { "real", "city", "united", "manchester", "athletic", "atletico", "inter", "sporting", "dynamo", "sport",
        "town", "rovers", "wanderers", "olympic", "saint", "international", "racing", "union", "borussia", "north", "south", "east", "west" };
    static readonly Dictionary<string, string> Alias = new() { ["utd"] = "united", ["man"] = "manchester", ["intl"] = "international", ["st"] = "saint", ["munchen"] = "munich", ["wolves"] = "wolverhampton" };
    // squad markers: a youth/women/reserve side must only match the same kind of side
    static readonly Regex Marker = MarkerRegex();
    [GeneratedRegex(@"^(u\d{2}|w|women|ladies|res|reserves|ii|iii|b)$")] private static partial Regex MarkerRegex();

    // different spellings of the same kind of side
    static readonly Dictionary<string, string> MarkerClass = new()
    { ["res"] = "reserve", ["reserves"] = "reserve", ["u21"] = "reserve", ["u23"] = "reserve", ["w"] = "women", ["women"] = "women", ["ladies"] = "women" };

    static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c == 'đ' || c == 'Đ' ? 'd' : c);
        return sb.ToString().ToLowerInvariant();
    }

    public static (HashSet<string> Words, HashSet<string> Markers) Tokens(string name)
    {
        var words = new HashSet<string>(); var markers = new HashSet<string>();
        foreach (var raw in Regex.Split(Fold(name), @"[^a-z0-9]+").Where(t => t.Length > 0))
        {
            var t = Alias.TryGetValue(raw, out var a) ? a : raw;
            if (Marker.IsMatch(t)) markers.Add(MarkerClass.TryGetValue(t, out var c) ? c : t);
            else if (!Stop.Contains(t)) words.Add(t);
        }
        return (words, markers);
    }

    static bool TokenEq(string a, string b) => a == b || (Math.Min(a.Length, b.Length) >= 4 && (a.StartsWith(b) || b.StartsWith(a)));

    /// <summary>0..1; 0 when the squad markers differ (e.g. Arsenal vs Arsenal U19).</summary>
    public static double Similarity(string a, string b)
    {
        var (wa, ma) = Tokens(a); var (wb, mb) = Tokens(b);
        if (!ma.SetEquals(mb) || wa.Count == 0 || wb.Count == 0) return 0;
        var hits = wa.Where(x => wb.Any(y => TokenEq(x, y))).ToList();
        int min = Math.Min(wa.Count, wb.Count);
        // a partial overlap (e.g. "Real Madrid" vs "Real Sociedad") needs at least one distinctive shared word
        if (hits.Count < min && hits.All(Weak.Contains)) return 0;
        return (double)hits.Count / min;
    }

    /// <summary>Score of how well a live match fits a Betfair event name "Home v Away"; 0 if it does not.</summary>
    public static double Score(string eventName, string home, string away)
    {
        var parts = Regex.Split(eventName, @"\s+v\s+", RegexOptions.IgnoreCase);
        if (parts.Length != 2) return 0;
        double h = Similarity(parts[0], home), a = Similarity(parts[1], away);
        return Math.Min(h, a) >= 0.4 && (h + a) / 2 >= 0.7 ? (h + a) / 2 : 0;
    }
}

/// <summary>Connects to a live-score relay (WebSocket, JSON snapshot/goal/var events) and keeps the latest scores.</summary>
public sealed class LiveScoreFeed : IDisposable
{
    readonly object _gate = new();
    List<LiveMatch> _matches = new();
    readonly List<LiveAlert> _alerts = new();
    CancellationTokenSource? _cts;

    public bool Connected { get; private set; }
    public string? Url { get; private set; }
    public event Action? Changed;
    public event Action<LiveAlert>? Alert;
    /// <summary>A known match's score changed (goal, or a goal taken away). Arguments: new match, previous home and away score.</summary>
    public event Action<LiveMatch, int, int>? ScoreChanged;
    readonly Dictionary<string, (int H, int A)> _lastScore = new();

    public IReadOnlyList<LiveMatch> Matches { get { lock (_gate) return _matches.ToList(); } }
    public IReadOnlyList<LiveAlert> Alerts { get { lock (_gate) return _alerts.ToList(); } }

    public void Start(string url)
    {
        Url = url; _cts = new CancellationTokenSource();
        _ = Task.Run(() => Run(url, _cts.Token));
    }

    async Task Run(string url, CancellationToken ct)
    {
        int delay = 2;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(url), ct);
                Connected = true; delay = 2; Changed?.Invoke();
                var buf = new byte[64 * 1024]; var sb = new StringBuilder();
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var r = await ws.ReceiveAsync(buf, ct);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    if (r.EndOfMessage) { Handle(sb.ToString()); sb.Clear(); }
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* relay down or unreachable: retry */ }
            Connected = false; Changed?.Invoke();
            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); } catch { break; }
            delay = Math.Min(delay * 2, 30);
        }
    }

    /// <summary>Applies one relay message. Public so it can be tested without a socket.</summary>
    public void Handle(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            if (type == "snapshot" && root.TryGetProperty("matches", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var list = arr.EnumerateArray().Select(m => ToMatch(m)).Where(m => m != null).Cast<LiveMatch>().ToList();
                List<(LiveMatch, int, int)> changes;
                lock (_gate) { _matches = list; changes = list.Select(Track).Where(c => c.HasValue).Select(c => c!.Value).ToList(); }
                foreach (var (m, ph, pa) in changes) ScoreChanged?.Invoke(m, ph, pa);
                Changed?.Invoke();
                return;
            }
            var match = ToMatch(root);
            (LiveMatch, int, int)? change = null;
            if (match != null)
            {
                lock (_gate)
                {
                    int i = _matches.FindIndex(x => x.Home == match.Home && x.Away == match.Away);
                    if (i >= 0) _matches[i] = match; else _matches.Add(match);
                    change = Track(match);
                }
            }
            if (type is "goal" or "var")
            {
                var scorer = root.TryGetProperty("scoring_team", out var st) ? st.GetString() : null;
                var text = match != null
                    ? $"{type.ToUpperInvariant()}  {match.Home} {match.HomeScore}-{match.AwayScore} {match.Away}"
                      + (scorer == "home" ? $"  ({match.Home})" : scorer == "away" ? $"  ({match.Away})" : "")
                    : $"{type.ToUpperInvariant()}  {json}";
                var alert = new LiveAlert(DateTime.UtcNow, type, text, match?.Home, match?.Away);
                lock (_gate) { _alerts.Add(alert); if (_alerts.Count > 200) _alerts.RemoveAt(0); }
                Alert?.Invoke(alert);
            }
            if (change is var (cm, ch, ca)) ScoreChanged?.Invoke(cm, ch, ca);
            Changed?.Invoke();
        }
        catch (JsonException) { /* ignore malformed message */ }
    }

    /// <summary>Remembers each match's score; returns the previous score when it changed (never on first sight). Call under the lock.</summary>
    (LiveMatch, int, int)? Track(LiveMatch m)
    {
        var key = m.Home + "|" + m.Away;
        var had = _lastScore.TryGetValue(key, out var prev);
        _lastScore[key] = (m.HomeScore, m.AwayScore);
        return had && (prev.H != m.HomeScore || prev.A != m.AwayScore) ? (m, prev.H, prev.A) : null;
    }

    static LiveMatch? ToMatch(JsonElement m)
    {
        if (m.ValueKind != JsonValueKind.Object || !m.TryGetProperty("home_team", out var h) || !m.TryGetProperty("away_team", out var a)) return null;
        int Score(string k) => m.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        long id = m.TryGetProperty("sofa_event_id", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt64() : 0;
        return new LiveMatch(id, h.GetString() ?? "", a.GetString() ?? "", Score("home_score"), Score("away_score"), DateTime.UtcNow);
    }

    /// <summary>Best live match for a Betfair event name, or null.</summary>
    public LiveMatch? Find(string eventName)
    {
        LiveMatch? best = null; double bestScore = 0;
        foreach (var m in Matches)
        {
            var s = TeamMatcher.Score(eventName, m.Home, m.Away);
            if (s > bestScore) { best = m; bestScore = s; }
        }
        return best;
    }

    public void Stop() { _cts?.Cancel(); Connected = false; Url = null; lock (_gate) { _matches = new(); _lastScore.Clear(); } }

    public void Dispose() { _cts?.Cancel(); }
}
